// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A CDN origin pointing at a storage account. The origin's host name depends on which cloud the
// template is deployed to, which the template reads from environment() rather than hard-coding.

param accountName string

resource profile 'Microsoft.Cdn/profiles@2023-05-01' = {
  name: '${accountName}-cdn'
  location: 'global'
  sku: {
    name: 'Standard_Microsoft'
  }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2023-05-01' = {
  parent: profile
  name: 'assets'
  properties: {}
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2023-05-01' = {
  parent: originGroup
  name: 'blob'
  properties: {
    hostName: '${accountName}.blob.${environment().suffixes.storage}'
  }
}

output blobEndpoint string = 'https://${accountName}.blob.${environment().suffixes.storage}/'
