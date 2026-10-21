#!/usr/bin/env python3
"""Publish newest complete Unity UDP/JPEG frame with sensor-data QoS."""

import json
import math
import queue
import socket
import struct
import threading
import time

import cv2
import numpy as np
import rclpy
from rclpy.node import Node
from rclpy.qos import DurabilityPolicy, HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CameraInfo, CompressedImage, Image
from std_msgs.msg import String


SENSOR_QOS = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=1,
    reliability=ReliabilityPolicy.BEST_EFFORT,
    durability=DurabilityPolicy.VOLATILE,
)
CHUNK_HEADER = struct.Struct("<4sQHHI")
CHUNK_MAGIC = b"RMF1"
CHUNK_BYTES = 60000
MAX_FRAME_BYTES = 8_000_000


class LatestFrameAssembler:
    """Discard incomplete/old frames instead of delaying live control."""

    def __init__(self):
        self.sequence = -1
        self.parts = []
        self.remaining = 0
        self.total_size = 0
        self.incomplete_frames = 0

    def add(self, datagram):
        if len(datagram) < CHUNK_HEADER.size:
            return None
        magic, sequence, index, count, total = CHUNK_HEADER.unpack_from(datagram)
        if (magic != CHUNK_MAGIC or total < 8 or total > MAX_FRAME_BYTES or
                count != math.ceil(total / CHUNK_BYTES) or index >= count):
            return None
        payload = datagram[CHUNK_HEADER.size:]
        expected = min(CHUNK_BYTES, total - index * CHUNK_BYTES)
        if len(payload) != expected:
            return None
        if sequence < self.sequence:
            return None
        if sequence > self.sequence:
            if self.sequence >= 0 and self.remaining:
                self.incomplete_frames += 1
            self.sequence = sequence
            self.parts = [None] * count
            self.remaining = count
            self.total_size = total
        if count != len(self.parts) or total != self.total_size:
            return None
        if self.parts[index] is None:
            self.parts[index] = payload
            self.remaining -= 1
        if self.remaining:
            return None
        frame = b"".join(self.parts)
        self.parts = []
        return frame


def unpack_frame(frame):
    if len(frame) < 8:
        raise ValueError("Short camera frame")
    header_size = struct.unpack_from("<I", frame, 0)[0]
    if header_size > 100_000 or 8 + header_size > len(frame):
        raise ValueError("Invalid camera header size")
    header = json.loads(frame[4:4 + header_size])
    image_size = struct.unpack_from("<I", frame, 4 + header_size)[0]
    jpeg = frame[8 + header_size:]
    if image_size != len(jpeg) or image_size < 2:
        raise ValueError("Invalid camera JPEG size")
    return header, jpeg


class UnityCameraBridge(Node):
    def __init__(self):
        super().__init__("unity_camera_bridge")
        self.declare_parameter("unity_host", "127.0.0.1")
        self.declare_parameter("unity_port", 15001)
        self.declare_parameter("publish_oracle_labels", False)
        self.declare_parameter("publish_raw_image", False)
        self._address = (
            str(self.get_parameter("unity_host").value),
            int(self.get_parameter("unity_port").value),
        )
        self._frames = queue.Queue(maxsize=1)
        self._stop = threading.Event()
        self._compressed_pub = self.create_publisher(
            CompressedImage, "/rm_energy/camera/image_raw/compressed", SENSOR_QOS
        )
        self._raw_pub = (self.create_publisher(
            Image, "/rm_energy/camera/image_raw", SENSOR_QOS
        ) if bool(self.get_parameter("publish_raw_image").value) else None)
        self._info_pub = self.create_publisher(
            CameraInfo, "/rm_energy/camera/camera_info", SENSOR_QOS
        )
        # Precise simulator keypoints are optional debug data.
        self._label_pub = (self.create_publisher(
            String, "/rm_energy/debug/labels", SENSOR_QOS
        ) if bool(self.get_parameter("publish_oracle_labels").value) else None)
        self.create_timer(0.005, self._publish_latest)
        self._reader = threading.Thread(target=self._read_loop, daemon=True)
        self._reader.start()
        self.get_logger().info(
            f"Waiting for Unity UDP camera at {self._address}; "
            f"JPEG topic best-effort depth=1, raw compatibility={'on' if self._raw_pub else 'off'}"
        )

    def _read_loop(self):
        while not self._stop.is_set():
            try:
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as stream:
                    stream.bind(("127.0.0.1", 0))
                    stream.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4_000_000)
                    stream.settimeout(0.25)
                    assembler = LatestFrameAssembler()
                    last_hello = 0.0
                    last_report = time.monotonic()
                    complete_count = 0
                    queue_drop_count = 0
                    last_incomplete = 0
                    while not self._stop.is_set():
                        now = time.monotonic()
                        if now - last_report >= 5.0:
                            self.get_logger().info(
                                f"UDP camera complete={complete_count / (now - last_report):.1f}Hz "
                                f"incomplete={assembler.incomplete_frames - last_incomplete} "
                                f"queue_drop={queue_drop_count}"
                            )
                            last_report = now
                            complete_count = 0
                            queue_drop_count = 0
                            last_incomplete = assembler.incomplete_frames
                        if now - last_hello >= 1.0:
                            stream.sendto(b"RMH1" + (b"\x01" if self._label_pub else b"\x00"), self._address)
                            last_hello = now
                        try:
                            datagram, sender = stream.recvfrom(CHUNK_HEADER.size + CHUNK_BYTES)
                        except socket.timeout:
                            continue
                        if sender[0] != self._address[0] or sender[1] != self._address[1]:
                            continue
                        frame = assembler.add(datagram)
                        if frame is None:
                            continue
                        header, jpeg = unpack_frame(frame)
                        complete_count += 1
                        try:
                            self._frames.put_nowait((header, jpeg))
                        except queue.Full:
                            try:
                                self._frames.get_nowait()
                                queue_drop_count += 1
                            except queue.Empty:
                                pass
                            self._frames.put_nowait((header, jpeg))
            except (OSError, ValueError, queue.Empty):
                if not self._stop.is_set():
                    time.sleep(0.5)

    def _publish_latest(self):
        try:
            header, jpeg = self._frames.get_nowait()
        except queue.Empty:
            return
        # Never spend decode/publish time on a frame already superseded in the
        # reader queue. Autoaim needs the freshest available observation.
        while True:
            try:
                header, jpeg = self._frames.get_nowait()
            except queue.Empty:
                break
        width = int(header["width"])
        height = int(header["height"])
        if width <= 0 or height <= 0:
            self.get_logger().warning("Invalid Unity camera dimensions")
            return

        stamp_ns = int(header["stamp_ns"])
        stamp_sec, stamp_nanosec = divmod(stamp_ns, 1_000_000_000)
        compressed_msg = CompressedImage()
        compressed_msg.header.stamp.sec = stamp_sec
        compressed_msg.header.stamp.nanosec = stamp_nanosec
        compressed_msg.header.frame_id = "camera_optical_frame"
        compressed_msg.format = "jpeg"
        compressed_msg.data = jpeg

        f = height / (2.0 * math.tan(math.radians(header["vertical_fov_deg"]) / 2.0))
        info = CameraInfo()
        info.header = compressed_msg.header
        info.width = width
        info.height = height
        info.distortion_model = "plumb_bob"
        info.d = [0.0] * 5
        info.k = [f, 0.0, width / 2.0, 0.0, f, height / 2.0, 0.0, 0.0, 1.0]
        info.r = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0]
        info.p = [f, 0.0, width / 2.0, 0.0,
                  0.0, f, height / 2.0, 0.0,
                  0.0, 0.0, 1.0, 0.0]

        labels = String()
        labels.data = json.dumps(header, separators=(",", ":"))
        self._info_pub.publish(info)
        self._compressed_pub.publish(compressed_msg)
        if self._label_pub is not None:
            self._label_pub.publish(labels)

        # Optional legacy endpoint. A 1920x1080 bgr8 sample is about 6.22 MB;
        # keep it opt-in so DDS fragmentation cannot delay the JPEG path.
        if self._raw_pub is not None:
            image = cv2.imdecode(np.frombuffer(jpeg, dtype=np.uint8), cv2.IMREAD_COLOR)
            if image is None or image.shape[:2] != (height, width):
                self.get_logger().warning("Could not decode Unity JPEG for raw compatibility")
                return
            image_msg = Image()
            image_msg.header = compressed_msg.header
            image_msg.height = height
            image_msg.width = width
            image_msg.encoding = "bgr8"
            image_msg.is_bigendian = 0
            image_msg.step = width * 3
            image_msg.data = image.tobytes()
            self._raw_pub.publish(image_msg)

    def destroy_node(self):
        self._stop.set()
        return super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = UnityCameraBridge()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.try_shutdown()


if __name__ == "__main__":
    main()
