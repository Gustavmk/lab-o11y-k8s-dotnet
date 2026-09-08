# Observabilidade para .NET 10 no Kubernetes local

Lab completo com **auto-instrumentação zero-code**, rastreamento distribuído, métricas RED e roteamento por domínio — tudo rodando num cluster [Kind](https://kind.sigs.k8s.io/).

## Arquitetura


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
kind create cluster --name dev --config=kind/config.yaml
```

### 3. Argocd

```bash
helm repo add argo https://argoproj.github.io/argo-helm
helm install argocd -n argocd argo/argo-cd --version 10.6.4 -f helm/argocd-values.yaml --create-namespace

# Get Secret
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d

kubectl apply -f application.yaml
```


### 4. Build e deploy da aplicação

Essa etapa é mandatatória para carregar a imagem container nos hosts do Kind. 

```bash
# Build da imagem + load no Kind + restart do deployment
./app/azure-voting-app-dotnet/build-and-load.sh
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

