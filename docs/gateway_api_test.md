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