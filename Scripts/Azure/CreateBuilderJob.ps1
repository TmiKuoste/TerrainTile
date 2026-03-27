
## Optional
#az login --use-device-code

#az upgrade
#az extension add --name containerapp --upgrade

az account show
#az account set --subscription ""
## /Optional

$PROJECT_NAME = "tile-proto"
$SB_QUEUE_NAME = "create-dem"
$USER_ASSIGNED_IDENTITY_NAME = "uami-for-jobs"
$LOCATION = "northeurope"
$CONTAINER_IMAGE_NAME = "$PROJECT_NAME-job:1.0" 

$PROJECT_NAME_WO_DASH = $PROJECT_NAME -replace "-", ""
$TIMESTAMP = Get-Date -Format "MMddHHmm"

$CONTAINER_REGISTRY_NAME = "acr" + $PROJECT_NAME_WO_DASH + $TIMESTAMP
$RESOURCE_GROUP = "rg-$PROJECT_NAME-$TIMESTAMP"
$SB_NAMESPACE = "sbns-$PROJECT_NAME-$TIMESTAMP"
$CONTAINER_APP_ENVIRONMENT_NAME ="cae-$PROJECT_NAME"
$JOB_NAME = "caj-$PROJECT_NAME"
$LOG_ANALYTICS_WORKSPACE_NAME = "la-$PROJECT_NAME"

$USER_ID = $(az ad signed-in-user show --query id --output tsv)
$SUBSCRIPTION_ID = $(az account show --query id --output tsv)

az group create -n $RESOURCE_GROUP -l $LOCATION

az servicebus namespace create `
    --resource-group $RESOURCE_GROUP `
    --name $SB_NAMESPACE `
    --location $LOCATION `
	--sku Basic

az servicebus queue create `
    --resource-group $RESOURCE_GROUP `
    --namespace-name $SB_NAMESPACE `
    --name $SB_QUEUE_NAME
	
## Create a shared access policy for the queue only if your container does not support managed identities
$SB_LISTEN_POLICY_NAME = "sap-listen"

az servicebus queue authorization-rule create `
    -g $RESOURCE_GROUP `
	--namespace-name $SB_NAMESPACE `
	--queue-name $SB_QUEUE_NAME `
	--name $SB_LISTEN_POLICY_NAME `
	--rights Listen
	
$QUEUE_CONNECTION_STRING = $(az servicebus queue authorization-rule keys list `
    --resource-group $RESOURCE_GROUP `
	--namespace-name $SB_NAMESPACE `
    --queue-name $SB_QUEUE_NAME `
	--name $SB_LISTEN_POLICY_NAME `
	--query "primaryConnectionString" `
	--output tsv)
	
## /Create a shared access policy


az identity create --resource-group $RESOURCE_GROUP --name $USER_ASSIGNED_IDENTITY_NAME

$IDENTITY_GUID = $(az identity show --name $USER_ASSIGNED_IDENTITY_NAME --resource-group $RESOURCE_GROUP --query principalId --output tsv)

$IDENTITY_ID = $(az identity show --name $USER_ASSIGNED_IDENTITY_NAME --resource-group $RESOURCE_GROUP --query id --output tsv)

az role assignment create `
    --assignee-object-id $IDENTITY_GUID `
	--assignee-principal-type ServicePrincipal `
	--role "Azure Service Bus Data Receiver" `
	--scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.ServiceBus/namespaces/$SB_NAMESPACE

az acr create `
    --name $CONTAINER_REGISTRY_NAME `
    --resource-group $RESOURCE_GROUP `
    --location $LOCATION `
    --sku Basic `
    --admin-enabled true
	
az acr build `
    --registry $CONTAINER_REGISTRY_NAME `
    --image $CONTAINER_IMAGE_NAME `
	"https://github.com/Kuoste/TerrainTile#:BuilderServices"
	
az provider register -n Microsoft.OperationalInsights --wait

## Optional
az monitor log-analytics workspace create `
  --resource-group $RESOURCE_GROUP `
  --workspace-name $LOG_ANALYTICS_WORKSPACE_NAME `
  --location $LOCATION
  
$LAW_ID = $(az monitor log-analytics workspace show `
    -g $RESOURCE_GROUP `
	--workspace-name $LOG_ANALYTICS_WORKSPACE_NAME `
	--query customerId  --output tsv)

$LAW_PRIMARY_KEY = $(az monitor log-analytics workspace get-shared-keys `
    --resource-group $RESOURCE_GROUP `
	--workspace-name $LOG_ANALYTICS_WORKSPACE_NAME `
	--query "primarySharedKey" --output tsv)
## /Optional

az containerapp env create `
    --name $CONTAINER_APP_ENVIRONMENT_NAME `
    --resource-group $RESOURCE_GROUP `
    --location $LOCATION `
    --logs-workspace-id $LAW_ID `
    --logs-workspace-key $LAW_PRIMARY_KEY
 
 az containerapp job create `
 --name $JOB_NAME `
 --resource-group $RESOURCE_GROUP `
 --mi-user-assigned $IDENTITY_ID `
 --environment $CONTAINER_APP_ENVIRONMENT_NAME `
 --trigger-type "Event" `
 --replica-timeout "1800" `
 --replica-retry-limit "1" `
 --replica-completion-count "1" `
 --parallelism "1" `
 --min-executions "0" `
 --max-executions "10" `
 --polling-interval "60" `
 --scale-rule-name "queue" `
 --scale-rule-type "azure-servicebus" `
 --scale-rule-metadata "queueName=$SB_QUEUE_NAME" "namespace=$SB_NAMESPACE" "messageCount=1" `
 --scale-rule-identity $IDENTITY_ID `
 --image "$CONTAINER_REGISTRY_NAME.azurecr.io/$CONTAINER_IMAGE_NAME" `
 --cpu "4" `
 --memory "8Gi" `
 --workload-profile-name "Consumption" `
 --secrets "connection-string-secret=$QUEUE_CONNECTION_STRING" `
 --registry-server "$CONTAINER_REGISTRY_NAME.azurecr.io" `
 --env-vars "AZURE_SERVICE_BUS_SB_QUEUE_NAME=$SB_QUEUE_NAME" "AZURE_SERVICE_BUS_CONNECTION_STRING=secretref:connection-string-secret"
 