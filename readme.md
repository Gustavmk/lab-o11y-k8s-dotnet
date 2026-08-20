# Setup

1. kube-prometheus-operator

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm install kube-prometheus-stack \
    -n kube-prometheus-stack oci://ghcr.io/prometheus-community/charts/kube-prometheus-stack \
    --version 88.5.2 \
    --create-namespace \
    --values kube-prometheus-stack.yaml    
```

2. Apply Prometheus

```bash
kubectl create namespace monitoring
kubectl apply -f prometheus.yaml
```

3. OpenTelemetry Operator

```bash
helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts
helm repo update
 
helm search repo open-telemetry --versions > otel-repo.txt

OTEL_VERSION=0.121.0 
```

3.2 Certmanager

```bash
helm repo add jetstack https://charts.jetstack.io

helm search repo jetstack --versions | head

CERTMANAGER_VERSION=v1.21.1
```

3.3 install charts

```bash
helm install \
    cert-manager jetstack/cert-manager \
    --namespace cert-manager \
    --create-namespace \
    --version $CERTMANAGER_VERSION \
    --set crds.enabled=true \
    --set startupapicheck.timeout="5m"


helm install opentelemetry-operator open-telemetry/opentelemetry-operator \
    --namespace opentelemetry-operator-system \
    --create-namespace \
    --version $OTEL_VERSION \
    --values=otel-operator.yaml
```

3.4 Apply Collector

kubectl apply -n monitoring -f collector-tracing.yaml


#TODO: deploy do auto instrumentation
#TODO: deploy grafana
#TODO: app para testar 
# https://github.com/marcel-dempers/docker-development-youtube-series/blob/master/monitoring/opentelemetry/kubernetes/README.md