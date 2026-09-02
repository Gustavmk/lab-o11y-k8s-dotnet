helm repo add argo https://argoproj.github.io/argo-helm
helm install argocd -n argocd argo/argo-cd --version 10.6.4 -f helm/argocd-values.yaml --create-namespace

# Get Secret
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d

kubectl apply -f application.yaml