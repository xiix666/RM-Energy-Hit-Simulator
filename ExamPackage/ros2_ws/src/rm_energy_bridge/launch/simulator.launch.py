from launch import LaunchDescription
from launch_ros.actions import Node


def generate_launch_description():
    return LaunchDescription([
        Node(package="rm_energy_bridge", executable="unity_camera_bridge",
             name="unity_camera_bridge", output="screen",
             parameters=[{"publish_oracle_labels": True}]),
        Node(package="rm_energy_bridge", executable="unity_udp_bridge",
             name="unity_udp_bridge", output="screen"),
    ])
