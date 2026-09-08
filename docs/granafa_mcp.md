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


1. Criando conta de serviço

GRAFANA_PASS=$(kubectl get secret --namespace grafana grafana -o jsonpath="{.data.admin-password}" | base64 --decode ; echo) 
echo $GRAFANA_PASS
GRAFANA_URL='grafana.kubernetes.docker.internal:8080'

curl -X POST -H "Content-Type: application/json" -d '{"name":"meu-bot", "role": "Admin"}' http://admin:$GRAFANA_PASS@$GRAFANA_URL/api/serviceaccounts

2. Criando token 

curl -X POST -H "Content-Type: application/json" -d '{"name":"meu-token-api"}' http://admin:$GRAFANA_PASS@$GRAFANA_URL/api/serviceaccounts/3/tokens

