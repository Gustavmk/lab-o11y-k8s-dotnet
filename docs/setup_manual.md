
### 3. kube-prometheus-stack (apenas o Operator)

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm upgrade --install kube-prometheus-stack \
  -n kube-prometheus-stack \
  oci://ghcr.io/prometheus-community/charts/kube-prometheus-stack \
  --version 88.5.2 \
  --create-namespace \
  --values helm/values-kube-prometheus-stack.yaml
```

### 4. OpenTelemetry Operator

```bash
# cert-manager (dependência do webhook do OTel Operator)
helm repo add jetstack https://charts.jetstack.io
CERTMANAGER_VERSION=v1.21.1
helm install cert-manager jetstack/cert-manager \
  --namespace cert-manager \
  --create-namespace \
  --version $CERTMANAGER_VERSION \
  --set crds.enabled=true \
  --set startupapicheck.timeout="5m"

# OTel Operator
helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts
helm repo update
OTEL_VERSION=0.121.0
helm install opentelemetry-operator open-telemetry/opentelemetry-operator \
  --namespace opentelemetry-operator-system \
  --create-namespace \
  --version $OTEL_VERSION \
  --values helm/values-otel-operator.yaml
```

### 5. Tempo (datastore de traces)

```bash
helm repo add grafana https://grafana.github.io/helm-charts
helm repo update
TEMPO_VERSION=1.24.4
helm upgrade --install tempo grafana/tempo \
  --create-namespace \
  --namespace grafana \
  --version $TEMPO_VERSION \
  --values helm/values-tempo.yaml
```

O `values-tempo.yaml` habilita `service-graphs`, `span-metrics` e **`local-blocks`** — necessários para métricas TraceQL e o Grafana Traces Drilldown.

### 6. Grafana

```bash
GRAFANA_VERSION=10.5.15
helm upgrade --install grafana grafana/grafana \
  --namespace grafana \
  --version $GRAFANA_VERSION \
  --values helm/values-grafana.yaml
```

Datasources provisionados automaticamente: **Prometheus** e **Tempo**.

### 7. Manifestos base

```bash
kubectl apply -f manifestos/
```

Inclui: namespace `monitoring`, Prometheus CR, OTel Collector, Instrumentation CR e Gateway + HTTPRoutes.

### 8. Build e deploy da aplicação

```bash
# Build da imagem + load no Kind + restart do deployment
./app/azure-voting-app-dotnet/build-and-load.sh

# Deploy dos manifests Kubernetes
kubectl apply -f app/azure-voting-app-dotnet/kubernetes-manifests/