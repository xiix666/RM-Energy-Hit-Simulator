#!/usr/bin/env python3
"""Relay public ROS 2 gimbal commands to the local Unity process over UDP."""

import json
import socket
import uuid

import rclpy
from rclpy.node import Node

from rm_energy_interfaces.msg import GimbalCommand


class UnityUdpBridge(Node):
    def __init__(self) -> None:
        super().__init__("unity_udp_bridge")
        self.declare_parameter("unity_host", "127.0.0.1")
        self.declare_parameter("unity_port", 15000)
        self._destination = (
            str(self.get_parameter("unity_host").value),
            int(self.get_parameter("unity_port").value),
        )
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._session_id = uuid.uuid4().hex
        self._sequence = 0
        self._subscription = self.create_subscription(
            GimbalCommand,
            "/rm_energy/gimbal/command",
            self._on_command,
            10,
        )
        self.get_logger().info(
            f"Relaying /rm_energy/gimbal/command to udp://"
            f"{self._destination[0]}:{self._destination[1]}"
        )

    def _on_command(self, message: GimbalCommand) -> None:
        packet = {
            "session_id": self._session_id,
            "seq": self._sequence,
            "yaw_rad": float(message.yaw_rad),
            "pitch_rad": float(message.pitch_rad),
            "fire": bool(message.fire),
        }
        self._sequence += 1
        self._socket.sendto(
            json.dumps(packet, separators=(",", ":")).encode("utf-8"),
            self._destination,
        )

    def destroy_node(self):
        self._socket.close()
        return super().destroy_node()


def main(args=None) -> None:
    rclpy.init(args=args)
    node = UnityUdpBridge()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.try_shutdown()


if __name__ == "__main__":
    main()
