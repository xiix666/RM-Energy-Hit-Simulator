#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
unity_editor="${UNITY_EDITOR:-}"

if [[ -z "$unity_editor" ]]; then
  candidates=(
    "$HOME/Unity/Hub/Editor/2021.3.42f1/Editor/Unity"
    "$HOME/Unity/Hub/Editor/2021.3.42f1c1/Editor/Unity"
    "/opt/unity/Editor/Unity"
  )
  for candidate in "${candidates[@]}"; do
    if [[ -x "$candidate" ]]; then unity_editor="$candidate"; break; fi
  done
fi

if [[ ! -x "$unity_editor" ]]; then
  echo "未找到 Unity 2021.3.42f1 编辑器。" >&2
  echo "请通过 Unity Hub 安装，或设置 UNITY_EDITOR=/absolute/path/to/Editor/Unity。" >&2
  exit 1
fi

editor_dir="$(cd -- "$(dirname -- "$unity_editor")" && pwd)"
unity_ssl_dir="$editor_dir/BugReporter"
if [[ -d "$unity_ssl_dir" ]]; then
  export LD_LIBRARY_PATH="$unity_ssl_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

if pgrep -af "[U]nity" | grep -F -- "$project_dir" >/dev/null 2>&1; then
  echo "该项目已经在 Unity 中打开。请关闭现有窗口后重新运行，以保证自动进入 Play。" >&2
  exit 2
fi

mkdir -p "$project_dir/Logs"
log_path="$project_dir/Logs/EnergySimulatorAutoStart.log"
nohup "$unity_editor" -projectPath "$project_dir" -energySimulatorAutoPlay -logFile "$log_path" >/dev/null 2>&1 &
unity_pid=$!

echo "RM Energy Hit Simulator 正在启动（PID: $unity_pid）"
echo "Unity 完成导入和编译后会自动进入 Play。"
echo "日志：$log_path"
