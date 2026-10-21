from glob import glob
from pathlib import Path
from setuptools import find_packages, setup


package_name = "rm_energy_bridge"

setup(
    name=package_name,
    version="0.1.0",
    packages=find_packages(exclude=["test"]),
    data_files=[
        ("share/ament_index/resource_index/packages", ["resource/" + package_name]),
        ("share/" + package_name, ["package.xml"]),
        ("share/" + package_name + "/launch", glob("launch/*.launch.py")),
        ("share/" + package_name + "/config", [str(Path("../../../config/public_data.yaml"))]),
    ],
    install_requires=["setuptools"],
    zip_safe=True,
    maintainer="RM Energy Simulator Maintainer",
    maintainer_email="maintainer@example.com",
    description="Public ROS 2 camera and command relay for the energy simulator.",
    license="Apache-2.0",
    entry_points={
        "console_scripts": [
            "unity_udp_bridge = rm_energy_bridge.unity_udp_bridge:main",
            "unity_camera_bridge = rm_energy_bridge.unity_camera_bridge:main",
        ],
    },
)
