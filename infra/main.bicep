@description('Nom de base pour toutes les ressources')
param appName string = 'aparthunter'

@description('Région Azure')
param location string = resourceGroup().location

@description('Numéro de téléphone destinataire SMS')
@secure()
param recipientPhone string

@description('OVH App Key')
@secure()
param ovhAppKey string

@description('OVH App Secret')
@secure()
param ovhAppSecret string

@description('OVH Consumer Key')
@secure()
param ovhConsumerKey string

@description('OVH Service Name')
param ovhServiceName string

// Storage Account (requis par Azure Functions + Table Storage pour les annonces vues)
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: '${appName}${uniqueString(resourceGroup().id)}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// Application Insights
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// Consumption Plan (serverless)
resource hostingPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${appName}-plan'
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: {}
}

// Azure Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: '${appName}-func'
  location: location
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: hostingPlan.id
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTSHARE', value: '${appName}-func' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        // Critères de recherche
        { name: 'SearchCriteria__Arrondissements__0', value: '10' }
        { name: 'SearchCriteria__Arrondissements__1', value: '11' }
        { name: 'SearchCriteria__Arrondissements__2', value: '12' }
        { name: 'SearchCriteria__Arrondissements__3', value: '13' }
        { name: 'SearchCriteria__Arrondissements__4', value: '18' }
        { name: 'SearchCriteria__Arrondissements__5', value: '19' }
        { name: 'SearchCriteria__Arrondissements__6', value: '20' }
        { name: 'SearchCriteria__PriceMin', value: '1800' }
        { name: 'SearchCriteria__PriceMax', value: '2500' }
        { name: 'SearchCriteria__RoomsMin', value: '3' }
        { name: 'SearchCriteria__RoomsMax', value: '3' }
        // OVH SMS — à compléter après déploiement via le portail Azure (App Settings)
        { name: 'OvhSms__AppKey', value: ovhAppKey }
        { name: 'OvhSms__AppSecret', value: ovhAppSecret }
        { name: 'OvhSms__ConsumerKey', value: ovhConsumerKey }
        { name: 'OvhSms__ServiceName', value: ovhServiceName }
        { name: 'OvhSms__SenderName', value: 'Appart' }
        { name: 'OvhSms__RecipientPhoneNumber', value: recipientPhone }
        // URLs scrapers — à configurer via le portail Azure après déploiement
        { name: 'Scrapers__Pap__Enabled', value: 'true' }
        { name: 'Scrapers__Pap__RssUrl', value: 'CONFIGURER' }
        { name: 'Scrapers__LeBonCoin__Enabled', value: 'true' }
        { name: 'Scrapers__LeBonCoin__SearchUrl', value: 'CONFIGURER' }
        { name: 'Scrapers__SeLoger__Enabled', value: 'true' }
        { name: 'Scrapers__SeLoger__SearchUrl', value: 'CONFIGURER' }
        { name: 'Scrapers__Jinka__Enabled', value: 'false' }
        { name: 'Scrapers__Jinka__SearchUrl', value: 'CONFIGURER' }
        { name: 'Scrapers__Jinka__SessionCookie', value: '' }
        { name: 'Scrapers__GensDeConfiance__Enabled', value: 'false' }
        { name: 'Scrapers__GensDeConfiance__SearchUrl', value: 'CONFIGURER' }
        { name: 'Scrapers__GensDeConfiance__SessionCookie', value: '' }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
    httpsOnly: true
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
