param role string

// A module sees the deployment name its own declaration computed, not the root's.
output stamp string = '${deployment().name}/${role}'
