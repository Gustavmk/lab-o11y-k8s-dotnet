# Do zero à observabilidade completa: .NET 10 no Kubernetes local com OpenTelemetry, Tempo e Grafana

> **TL;DR** — Construí um lab local com Kind que demonstra auto-instrumentação zero-code para .NET 10, rastreamento distribuído com Tempo, métricas RED no Prometheus e roteamento HTTP por domínio via Gateway API. No final, adicionei servidores MCP de Grafana e Kubernetes para consultar o ambiente com linguagem natural dentro do VS Code.

---

## Por que esse projeto existe

Observabilidade em Kubernetes costuma ser ensinada com exemplos simplificados que omitem as partes mais trabalhosas: configurar o pipeline de coleta, entender como os dados fluem entre sistemas e lidar com os problemas reais que aparecem no caminho. A ideia aqui foi montar esse ambiente do zero, documentar cada decisão e registrar os erros — que foram muitos e muito instrutivos.

---

## A trajetória: o que os commits contam

### Fase 1 — App e infraestrutura básica

O ponto de partida foi um app de votação clássico (Azure Vote App), originalmente em Python. A primeira decisão foi migrar para **.NET 10** para ter uma stack mais representativa de workloads reais de empresa.

```
9581c63  wip commit
92f5bec  Add Kubernetes deployment and service configurations
2f3ab92  azure vote app - migrate to dotnet 10
```

Nessa fase o app é instrumentado manualmente via SDK: `ActivitySource`, `Meter`, `AddAspNetCoreInstrumentation()`, `AddRedisInstrumentation()`. Funciona bem para desenvolvimento, mas exige mudança de código para cada serviço.

### Fase 2 — Infraestrutura local funcional

```
25aa902  Update launch and task configurations for .NET 10
e8df5f0  Remove unused VSCode settings; add build-and-load script
22a8de6  docker compose para testes locais sem cluster
```

Aqui surgiu a necessidade de um fluxo rápido de desenvolvimento. O script `build-and-load.sh` resolve o problema clássico do Kind: imagens locais não ficam disponíveis automaticamente no cluster, é preciso fazer `kind load docker-image` explicitamente. O script encapsula o build Docker, o load e o rollout restart em um único comando.

O Docker Compose paralelo permite testar o app sem precisar do cluster quando só se quer iterar na lógica de negócio.

### Fase 3 — Gateway API e roteamento por domínio

```
b551f55  Add Gateway API configuration and update README
```

Esse foi o commit mais denso em aprendizado. A Gateway API é a evolução do Ingress no Kubernetes, mas num cluster Kind ela não tem um LoadBalancer real. A solução é o `cloud-provider-kind`: um container Docker que roda fora do cluster e faz o bridge entre `*.kubernetes.docker.internal` e o IP do node Kind.

**O ponto que gerou mais confusão**: o Gateway listener fica na porta **8080**, não na 80. O DNS resolve `vote-app.kubernetes.docker.internal` corretamente para o IP do Gateway (`172.18.0.3`), mas `curl http://vote-app.kubernetes.docker.internal` (porta 80 por padrão) retorna `Connection refused`. A porta correta é sempre **:8080**.

```bash
# ❌ não funciona
curl http://vote-app.kubernetes.docker.internal

# ✅ funciona
curl http://vote-app.kubernetes.docker.internal:8080
```

### Fase 4 — Stack de observabilidade completa

```
8686f7e  Add OpenTelemetry dashboard for HTTP services
8220a9e  Increase resource requests and limits
```

A stack foi montada com:

- **Prometheus Operator** (apenas o operator, sem Grafana nem Alertmanager bundled)
- **cert-manager** (dependência do webhook do OpenTelemetry Operator)
- **OpenTelemetry Operator** — o componente que muda o jogo
- **Tempo** — armazenamento de traces com suporte a TraceQL
- **Grafana** — visualização com datasources provisionados via Helm values

A annotation `instrumentation.opentelemetry.io/inject-dotnet: "true"` no pod faz o Operator injetar o agente OTel automaticamente via init container. **Zero mudança de código, zero rebuild da imagem.**

O pipeline de dados ficou assim:

```
app → OTel Collector → Tempo (traces)
                     → Prometheus remote write (métricas)
                     
Tempo metricsGenerator → Prometheus (service graphs, span metrics)
```

#### O problema do OOMKilled

O agente .NET do OpenTelemetry adiciona aproximadamente 150 MB de overhead em runtime. O deployment original tinha `limits.memory: 100Mi` — resultado: 20 restarts por OOMKilled em menos de 8 horas.

```bash
kubectl describe pod -l app=azure-vote-app | grep -A5 "Last State"
# Last State: Terminated
#   Reason: OOMKilled
#   Exit Code: 137
```

Aumentar para `256Mi` resolveu. Lição: ao usar auto-instrumentação, sempre dimensionar o pod levando em conta o overhead do agente.

#### O erro `localblocks processor not found`

O Grafana Traces Drilldown usa **métricas TraceQL** que dependem do processador `local-blocks` do Tempo. Sem ele, qualquer query de métricas retorna esse erro. A configuração no `values-tempo.yaml`:

```yaml
tempo:
  overrides:
    defaults:
      metrics_generator:
        processors:
        - service-graphs
        - span-metrics
        - local-blocks   # ← necessário para TraceQL metrics
```

### Fase 5 — Dashboard e correções

```
b108f74  Implement code changes to enhance functionality
d9ff9ac  fix: datasource
```

O dashboard **OpenTelemetry for HTTP services** exibe métricas RED (Rate, Errors, Duration) por endpoint com correlação direta para os traces no Tempo. Mas as variáveis `service_namespace` e `service_name` não funcionavam porque o dashboard foi construído assumindo um modelo de labels diferente do que o Prometheus usa na prática.

O label `job` no Prometheus tem o formato `namespace/service` (ex: `default/azure-vote-app`). As variáveis originais tentavam usar um label `exported_job` que não existe. A correção foi usar regex no próprio campo de variável do Grafana para extrair as partes:

```
service_namespace: label_values(metric, job)  →  regex: /^([^\/]+)\/.+/
service_name:      label_values(metric{job=~"${service_namespace}/.+"}, job)  →  regex: /^[^\/]+\/(.+)/
```

### Fase 6 — MCP para observabilidade assistida por AI

```
746234f  grafana mcp
```

O passo final foi adicionar servidores MCP (Model Context Protocol) ao workspace:

- **`mcp-grafana`** (Grafana oficial): acesso a dashboards, Prometheus, Tempo, alertas e muito mais via linguagem natural no GitHub Copilot
- **`kubernetes-mcp-server`** (containers/kubernetes-mcp-server): inspecionar pods, deployments, logs, eventos do cluster

Com isso, é possível perguntar diretamente no chat do Copilot:

> *"Qual é o P95 de latência do azure-vote-app na última hora?"*

E o MCP executa a query PromQL e retorna o resultado sem sair do editor.

---

## O ambiente em números (estado atual)

Dados consultados via MCP Grafana em tempo real:

| Métrica | Valor |
|---|---|
| Taxa de requisições HTTP 200 | ~0.06 req/s |
| P95 de latência (última hora) | ~200ms (picos durante restarts) |
| Datasources configurados | 2 (Prometheus + Tempo) |
| Dashboards | 1 (OpenTelemetry for HTTP services, 5 painéis) |
| Pods rodando | 11 |
| Restarts do azure-vote-app | 20 (todos por OOMKilled, resolvido) |

---

## O que aprendi

### 1. Gateway API no Kind precisa de `cloud-provider-kind`

Sem ele, Services do tipo `LoadBalancer` ficam em `<pending>` para sempre. O `cloud-provider-kind` é um container que roda fora do cluster e age como cloud provider, atribuindo IPs reais (da rede Docker) para os LoadBalancers.

### 2. Auto-instrumentação tem custo de memória

O agente OTel .NET injeta bibliotecas de instrumentação em runtime. Dimensionar o pod sem considerar isso leva a OOMKilled. Regra prática: adicionar pelo menos 150 MB ao que a aplicação usaria sem o agente.

### 3. Tempo precisa do `local-blocks` para TraceQL metrics

Não é óbvio na documentação padrão. O erro `localblocks processor not found` só aparece quando você tenta usar o Grafana Traces Drilldown. A solução é habilitar o processador no values do Helm.

### 4. O formato do label `job` afeta dashboards

O Prometheus, quando coleta via `ServiceMonitor` ou PodMonitor, popula o label `job` como `namespace/service`. Dashboards importados de fontes externas costumam assumir que `job` contém apenas o nome do serviço. Entender esse detalhe poupa horas de debug.

### 5. MCP transforma a forma de trabalhar com observabilidade

Ter o Grafana e o Kubernetes acessíveis via linguagem natural no Copilot acelera muito o ciclo de investigação. Em vez de alternar entre terminal, browser e editor, é possível fazer perguntas diretamente no contexto de onde está trabalhando.

---

## Arquitetura final

```
┌──────────────────── Kind Cluster ────────────────────────────────────────┐
│                                                                          │
│  ┌─────────────────┐     annotation: inject-dotnet=true                 │
│  │  azure-vote-app │──────────────────────────────────────────────────► │
│  │  (.NET 10 Blazor│  OTel Agent (init container)                       │
│  │   + Redis)      │  ↓ OTLP/gRPC                                       │
│  └─────────────────┘  ↓                                                 │
│                    OTel Collector                                        │
│                    ├── traces  ──► Tempo ──────► Grafana                 │
│                    └── metrics ──► Prometheus ──► Grafana                │
│                                                                          │
│  Gateway API (cloud-provider-kind)                                       │
│  ├── vote-app.kubernetes.docker.internal:8080 ──► azure-vote-app        │
│  └── grafana.kubernetes.docker.internal:8080  ──► grafana               │
└──────────────────────────────────────────────────────────────────────────┘

GitHub Copilot (VS Code)
├── MCP grafana     ──► Grafana API (dashboards, queries, alertas)
└── MCP kubernetes  ──► kubectl (pods, logs, eventos)
```

---

## Reproduzindo o ambiente

O repositório está disponível com toda a configuração Helm, manifestos e scripts. O setup completo leva cerca de 15 minutos numa máquina com Docker e Kind instalados.

Pré-requisitos: `kind`, `kubectl`, `helm`, `docker`.

```bash
git clone <repo>
cd labs
# siga o readme.md
```

---

## Próximos passos

- **Loki**: adicionar coleta de logs para completar os três pilares (metrics, traces, logs)
- **Kiali**: visualização do service mesh e topologia de serviços
- **Alertas**: configurar regras no Prometheus Alertmanager baseadas nas métricas RED
- **Multi-cluster**: testar o `kubernetes-mcp-server` com múltiplos contextos kubeconfig
