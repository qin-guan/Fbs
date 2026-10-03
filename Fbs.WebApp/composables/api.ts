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

/** The status the API answered a request with, if it did. */
export function getErrorStatus(error: unknown) {
  return error instanceof ResponseError ? error.status : undefined
}

/** The codes of the errors in problem details, such as `slug-taken`, which say what went wrong more exactly than the status. */
export function getErrorCodes(error: unknown) {
  return getProblemDetails(error)?.errors?.flatMap(e => (e.code ? [e.code] : [])) ?? []
}

/** What the errors in problem details say, to show. */
export function getErrorReasons(error: unknown) {
  return getProblemDetails(error)?.errors?.flatMap(e => (e.reason ? [e.reason] : [])) ?? []
}
