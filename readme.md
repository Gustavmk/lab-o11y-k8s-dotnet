# Reqs

1. Criação do cluster
    kind create cluster

2. Setup Gateway API 

doc: https://kubernetes.io/blog/2026/01/28/experimenting-gateway-api-with-kind/

```bash
VERSION="$(basename $(curl -s -L -o /dev/null -w '%{url_effective}' https://github.com/kubernetes-sigs/cloud-provider-kind/releases/latest))"
docker run -d --name cloud-provider-kind --rm --network host -v /var/run/docker.sock:/var/run/docker.sock registry.k8s.io/cloud-provider-kind/cloud-controller-manager:${VERSION}


# Verifique se está em execução
docker ps --filter name=cloud-provider-kind

# Get logs
docker logs cloud-provider-kind
```



# Setup

1. kube-prometheus-operator

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm upgrade --install kube-prometheus-stack \
    -n kube-prometheus-stack oci://ghcr.io/prometheus-community/charts/kube-prometheus-stack \
    --version 88.5.2 \
    --create-namespace \
    --values helm/values-kube-prometheus-stack.yaml    
```

2. OpenTelemetry Operator

```bash
helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts
helm repo update
 
helm search repo open-telemetry --versions > otel-repo.txt

OTEL_VERSION=0.121.0 


## CERT MANAGER
helm repo add jetstack https://charts.jetstack.io

helm search repo jetstack --versions | head

CERTMANAGER_VERSION=v1.21.1

## install charts
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
    --values helm/values-otel-operator.yaml
```

3. tracing data store (tempo)

```bash
helm repo add grafana https://grafana.github.io/helm-charts
helm repo update

helm search repo  grafana/tempo > tempo-version.txt

TEMPO_VERSION=1.24.4

helm install tempo grafana/tempo \
    --create-namespace \
    --namespace grafana \
    --version $TEMPO_VERSION \
    --values helm/values-tempo.yaml
```

4. Grafana


```bash
helm search repo grafana/grafana  > grafana-repo.txt

GRAFANA_VERSION=10.5.15

helm upgrade --install grafana grafana/grafana \
  --namespace grafana \
  --version $GRAFANA_VERSION \
  --values helm/values-grafana.yaml
```

5. Deploy manifests

`kubectl apply -f manifestos/`

6.  Build + Deploy App Dotnet
`app/azure-voting-app-dotnet/build-and-load.sh`
`k apply -f app/azure-voting-app-dotnet/kubernetes-manifests      `


## TODO: fale sobre inject de hosts para acessar os apps

file: hosts
127.0.0.1 grafana.kubernetes.docker.internal
127.0.0.1 vote-app.kubernetes.docker.internal


curl -I -v http://grafana.kubernetes.docker.internal:8080/ 