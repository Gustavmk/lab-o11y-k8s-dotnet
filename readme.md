# Observabilidade para .NET 10 no Kubernetes local

Lab completo com **auto-instrumentação zero-code**, rastreamento distribuído, métricas RED e roteamento por domínio — tudo rodando num cluster [Kind](https://kind.sigs.k8s.io/).

## Arquitetura

```
┌─────────────── Host (WSL2 / Linux) ──────────────────────────────────────┐
│                                                                          │
│  curl http://vote-app.kubernetes.docker.internal:8080                   │
│              │                                                           │
│  ┌───────────▼─────────────────────────────────────────────────────┐    │
│  │                     Kind Cluster                                │    │
│  │                                                                 │    │
│  │  cloud-provider-kind ──► Gateway (Envoy) :8080                 │    │
│  │                                   │                            │    │
│  │           ┌───────────────────────┤                            │    │
│  │     HTTPRoute                HTTPRoute                         │    │
│  │           ▼                       ▼                            │    │
│  │     azure-vote-app           grafana                           │    │
│  │     (Blazor/.NET 10)         (métricas + traces)               │    │
│  │           │ OTLP/gRPC                                          │    │
│  │           ▼                                                    │    │
│  │     OTel Collector                                             │    │
│  │      ├── traces ──► Tempo                                      │    │
│  │      └── metrics ──► Prometheus (remote write)                 │    │
│  │                           │                                    │    │
│  │     Tempo ──────────────► Grafana ◄── Prometheus               │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────────┘
```

### Componentes em execução

| Componente | Namespace | Função |
|---|---|---|
| azure-vote-app (.NET 10 Blazor) | default | App de exemplo instrumentada com OTel |
| azure-vote-redis | default | Backend de dados |
| OTel Collector | default + monitoring | OTLP → Tempo (traces) e Prometheus (métricas) |
| OpenTelemetry Operator | opentelemetry-operator-system | Injeta agente OTel via annotation |
| Tempo | grafana | Armazenamento de traces + TraceQL metrics |
| Prometheus | monitoring | Armazenamento de métricas |
| Prometheus Operator | kube-prometheus-stack | Gerencia CRDs do Prometheus |
| Grafana | grafana | Visualização e correlação métricas ↔ traces |
| Gateway (Envoy) | gateway-api-resources | Ingresso HTTP via Gateway API |
| cert-manager | cert-manager | Certificados para o webhook do OTel Operator |

---

## Pré-requisitos

```bash
kind     # https://kind.sigs.k8s.io/docs/user/quick-start/#installation
kubectl  # https://kubernetes.io/docs/tasks/tools/
helm     # https://helm.sh/docs/intro/install/
docker   # https://docs.docker.com/engine/install/
```

### `/etc/hosts` (uma vez só)

```
127.0.0.1 grafana.kubernetes.docker.internal
127.0.0.1 vote-app.kubernetes.docker.internal
```

---

## Setup passo a passo

### 1. Criar o cluster Kind

```bash
kind create cluster --config=kind/config.yaml
```

### 2. Gateway API — cloud-provider-kind

Provê um LoadBalancer real no Kind, expondo `*.kubernetes.docker.internal` na porta **8080**.

Ref: https://kubernetes.io/blog/2026/01/28/experimenting-gateway-api-with-kind/

```bash
VERSION="$(basename $(curl -s -L -o /dev/null -w '%{url_effective}' \
  https://github.com/kubernetes-sigs/cloud-provider-kind/releases/latest))"

docker run -d --name cloud-provider-kind --rm \
  --network host \
  -v /var/run/docker.sock:/var/run/docker.sock \
  registry.k8s.io/cloud-provider-kind/cloud-controller-manager:${VERSION}

# Verificar
docker ps --filter name=cloud-provider-kind
docker logs cloud-provider-kind
```

> **Atenção**: o Gateway listener fica na porta **8080**. Use sempre `http://host:8080` ao acessar pelo domínio.

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
```

A annotation `instrumentation.opentelemetry.io/inject-dotnet: "true"` no deployment aciona o operator para injetar o agente OTel automaticamente — **zero mudança de código**.

---

## Acessando os serviços

| Serviço | URL |
|---|---|
| Azure Vote App | http://vote-app.kubernetes.docker.internal:8080 |
| Grafana | http://grafana.kubernetes.docker.internal:8080 |

```bash
curl -I http://vote-app.kubernetes.docker.internal:8080
curl -I http://grafana.kubernetes.docker.internal:8080
```

**Credenciais Grafana**: `admin` / `admin`

---

## Pipeline de observabilidade

```
azure-vote-app
  │  OTLP/gRPC (injetado pelo OTel Operator — zero-code)
  ▼
OTel Collector (default/trace-collector)
  ├── traces  ──► Tempo  (gRPC :4317)
  ├── metrics ──► Prometheus (remote write :9090)
  └── logs    ──► debug stdout

Tempo (metricsGenerator)
  ├── service-graphs ──► Prometheus
  ├── span-metrics   ──► Prometheus
  └── local-blocks   ──► TraceQL metrics (Grafana Drilldown)
```

---

## Dashboard

**OpenTelemetry for HTTP services** — métricas RED (Rate, Errors, Duration) com correlação metrics ↔ traces.

Filtros por `service_namespace` e `service_name` derivados do label `job` (`namespace/service`).

---

## Desenvolvimento local (sem cluster)

```bash
# Docker Compose: app + redis localmente
cd app/azure-voting-app-dotnet
docker compose up

# Hot reload
dotnet watch run --project azure-vote/azure-vote/AzureVote.csproj
```

---

## MCP (Model Context Protocol)

O arquivo `.vscode/mcp.json` configura dois servidores MCP para uso com GitHub Copilot:

| Servidor | Ferramenta |
|---|---|
| `grafana` | `uvx mcp-grafana` — dashboards, Prometheus, Tempo, alertas |
| `kubernetes` | `npx kubernetes-mcp-server@latest` — recursos do cluster |

```bash
# Dependência para o servidor Grafana
curl -LsSf https://astral.sh/uv/install.sh | sh
```

Na primeira ativação, o VS Code solicita um **Service Account Token** do Grafana (Administration → Service Accounts → Add token).

---

## Troubleshooting

### `Connection refused` no curl

O Gateway escuta na porta **8080**, não 80. Use sempre `:8080`.

### `504 Gateway Timeout`

Pod em crash-loop por **OOMKilled**. O agente OTel .NET adiciona ~150 MB de overhead.

```bash
kubectl describe pod -l app=azure-vote-app | grep -A5 "Last State\|OOM"
```

O deployment define `limits.memory: 256Mi`.

### `localblocks processor not found` no Grafana

Adicionar ao `values-tempo.yaml` e fazer `helm upgrade`:

```yaml
tempo:
  overrides:
    defaults:
      metrics_generator:
        processors:
        - service-graphs
        - span-metrics
        - local-blocks
```

### Variáveis do dashboard não carregam

O label `job` tem o formato `namespace/service`. As variáveis usam regex:

- `service_namespace`: `/^([^\/]+)\/.+/`
- `service_name`: `/^[^\/]+\/(.+)/`