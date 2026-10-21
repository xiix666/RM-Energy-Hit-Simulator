# RM Energy Hit Simulator

基于 Unity 的 RoboMaster 能量机关击打仿真，提供弹丸、云台控制、命中统计及 ROS 2 相机与控制接口。

## 获取工程

需要 Git LFS 下载大型场地模型；普通 ZIP 下载可能只包含该模型的指针。

```bash
git lfs install
git clone https://github.com/xiix666/RM-Energy-Hit-Simulator.git
cd RM-Energy-Hit-Simulator
git lfs pull
```

使用 Unity Hub 中的 Unity 2021.3.42f1c1（原工程版本）打开。兼容的 2021.3.42f1 编辑器也可用于本机验证；切换版本前请备份。
打开 `Assets/Scenes/Init.unity`，点击 Play。Linux 上也可以运行：

```bash
./start_simulator.sh
```

脚本未找到编辑器时，可通过 `UNITY_EDITOR` 指定编辑器可执行文件。首次导入需下载 Unity Packages。

## ROS 2 接口

Ubuntu / ROS 2 Humble 环境中，在另一个终端构建并启动中继：

```bash
source /opt/ros/humble/setup.bash
cd ExamPackage/ros2_ws
colcon build --cmake-args -DPython3_EXECUTABLE=/usr/bin/python3 -DPYTHON_EXECUTABLE=/usr/bin/python3
source install/setup.bash
ros2 launch rm_energy_bridge simulator.launch.py
```

图像话题为 `/rm_energy/camera/image_raw/compressed`，内参为 `/rm_energy/camera/camera_info`。
向 `/rm_energy/gimbal/command` 发布 `rm_energy_interfaces/msg/GimbalCommand` 控制 yaw、pitch 和开火。
中继保留 `/rm_energy/debug/labels` 精准角点调试话题。请勿同时运行多个中继。

仓库包含仿真工程和公开中继，不包含内部文档、历史记录、截图数据和参考自瞄算法。
原项目参考 [RM_Labeling_Tool](https://github.com/Myshrimp/RM_Labeling_Tool)。
第三方代码及素材保留其原有许可和署名；仓库公开不意味着对第三方资源重新授权。
