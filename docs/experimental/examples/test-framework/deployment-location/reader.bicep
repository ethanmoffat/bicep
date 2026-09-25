targetScope = 'subscription'

param tag string

resource auditGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${tag}'
  location: 'westus2'
}

output stamp string = tag
