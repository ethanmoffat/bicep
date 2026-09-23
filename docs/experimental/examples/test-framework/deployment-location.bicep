targetScope = 'subscription'

param auditSubscriptionId string

// Nothing here mentions deployment(). Bicep emits "location": "[deployment().location]" for a
// module deployed to another subscription, so the template reads a deployment location its author
// never wrote.
module reader 'deployment-location/reader.bicep' = {
  name: 'audit-reader'
  scope: subscription(auditSubscriptionId)
  params: {
    tag: 'audit'
  }
}

output stamp string = reader.outputs.stamp
