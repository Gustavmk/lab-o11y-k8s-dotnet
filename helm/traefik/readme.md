helm repo add traefik https://traefik.github.io/charts
helm repo update


helm install traefik traefik/traefik -f helm/traefik/values.yaml --wait


az aks update --resource-group rg-myaks-dev --name aks-myaks-dev --enable-gateway-api