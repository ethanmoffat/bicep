// Module names are frequently derived from the deployment name so that concurrent deployments of the
// same template do not collide.
module primary 'deployment-name/stamp.bicep' = {
  name: '${deployment().name}-primary'
  params: {
    role: 'primary'
  }
}

module secondary 'deployment-name/stamp.bicep' = {
  name: '${deployment().name}-secondary'
  params: {
    role: 'secondary'
  }
}

output rootName string = deployment().name
output primaryStamp string = primary.outputs.stamp
output secondaryStamp string = secondary.outputs.stamp
