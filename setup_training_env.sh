#!/usr/bin/env bash
# =============================================================================
# ML-Agents Python trainer setup for this Unity project (Apple Silicon friendly)
# =============================================================================
# Unity project: com.unity.ml-agents 2.0.1 (see Packages/manifest.json)
#
# Official GitHub ML-Agents Release 18 pairs:
#   - Python: mlagents / ml-agents-envs v0.27.0 (install from this tag below)
#   - Unity package on that release tag: v2.1.0-exp.1 (not identical to 2.0.1,
#     but 2.0.1 is the same generation; trainer 0.27.0 is the usual match.)
#
# PyPI mlagents==0.27.0 pins torch<1.9 with no usable ARM wheels → install
# ml-agents-envs + ml-agents from the release_18 source tree, then let pip
# resolve torch against what you install first (CPU/MPS wheels).
#
# Requires: conda (https://docs.conda.io/en/latest/miniconda.html)
# Run: bash setup_training_env.sh
# =============================================================================
set -eu
set -o pipefail

ENV_NAME="ml-drone"
PYTHON_VER="3.9.13"
MLA_TAG="release_18"
CLONE_DIR="${TMPDIR:-/tmp}/ml-agents-${MLA_TAG}-$$"

if ! command -v conda &>/dev/null; then
  echo "ERROR: conda not found. Install Miniconda first: https://docs.conda.io/en/latest/miniconda.html"
  exit 1
fi

eval "$(conda shell.bash hook)"

if conda env list | awk '{print $1}' | grep -qx "${ENV_NAME}"; then
  echo "Conda env '${ENV_NAME}' already exists — skipping conda create."
  echo "To recreate: conda env remove -n ${ENV_NAME} -y && re-run this script."
else
  conda create -n "${ENV_NAME}" "python=${PYTHON_VER}" -y
fi

conda activate "${ENV_NAME}"

# PyTorch first (Apple Silicon: default index gives CPU/MPS-capable builds)
pip install --upgrade pip
pip install torch torchvision torchaudio

# Protobuf pin — reduces gRPC / tensorboard proto skew
pip install "protobuf==3.20.3"

# Install trainers from Unity's release_18 tag (matches 0.27.0 API; avoids PyPI torch pin)
rm -rf "${CLONE_DIR}"
git clone --branch "${MLA_TAG}" --depth 1 https://github.com/Unity-Technologies/ml-agents.git "${CLONE_DIR}"
pip install "${CLONE_DIR}/ml-agents-envs"
pip install "${CLONE_DIR}/ml-agents"
rm -rf "${CLONE_DIR}"

# Checkpoint / .onnx export uses torch.onnx and requires the onnx package.
pip install onnx six

echo ""
echo "=== Verification ==="
python --version
mlagents-learn --version || true
pip show mlagents mlagents-envs torch protobuf | sed -n '1,120p'

echo ""
echo "SUCCESS."
echo "  conda activate ${ENV_NAME}"
echo "  cd \"$(pwd)\""
echo "  mlagents-learn config/drone_training.yaml --run-id=reward_calibration"
echo "Then press Play in Unity (Remote training) to attach the environment."
