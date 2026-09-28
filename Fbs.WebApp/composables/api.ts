import { ResponseError, type FastEndpointsProblemDetails } from '~/api'

/**
 * The problem details the API sends when it rejects a request, such as validation errors or
 * refusing to cancel another unit's booking. Bare 401s and 404s have none.
 */
export function getProblemDetails(error: unknown) {
  if (error instanceof ResponseError && error.contentType?.includes('problem+json')) {
    return error.data as FastEndpointsProblemDetails
  }

  return undefined
}
