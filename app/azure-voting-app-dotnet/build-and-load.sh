#!/usr/bin/env bash
# Builds the azure-vote-app-dotnet Docker image and loads it into a local Kind cluster.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE_NAME="${IMAGE_NAME:-azure-vote-app-dotnet}"
IMAGE_TAG="${IMAGE_TAG:-local}"
KIND_CLUSTER="${KIND_CLUSTER:-kind}"
DEPLOYMENT_NAME="${DEPLOYMENT_NAME:-azure-vote-app}"
NAMESPACE="${NAMESPACE:-}"

IMAGE="${IMAGE_NAME}:${IMAGE_TAG}"

command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }
command -v kind >/dev/null || { echo "kind is required" >&2; exit 1; }

if ! kind get clusters | grep -qx "${KIND_CLUSTER}"; then
    echo "Kind cluster '${KIND_CLUSTER}' not found. Available clusters:" >&2
    kind get clusters >&2
    exit 1
fi

echo "==> Building ${IMAGE}"
docker build -f "${SCRIPT_DIR}/azure-vote/Dockerfile" -t "${IMAGE}" "${SCRIPT_DIR}/azure-vote"

echo "==> Loading ${IMAGE} into Kind cluster '${KIND_CLUSTER}'"
kind load docker-image "${IMAGE}" --name "${KIND_CLUSTER}"

if command -v kubectl >/dev/null && kubectl get deployment "${DEPLOYMENT_NAME}" ${NAMESPACE:+-n "${NAMESPACE}"} >/dev/null 2>&1; then
    echo "==> Restarting deployment '${DEPLOYMENT_NAME}' to pick up the new image"
    kubectl rollout restart deployment "${DEPLOYMENT_NAME}" ${NAMESPACE:+-n "${NAMESPACE}"}
else
    echo "==> Deployment '${DEPLOYMENT_NAME}' not found in the cluster; skipping restart"
    echo "    Set the deployment's image to '${IMAGE}' with imagePullPolicy: IfNotPresent to use it"
fi

echo "==> Done: ${IMAGE} is available inside Kind cluster '${KIND_CLUSTER}'"
