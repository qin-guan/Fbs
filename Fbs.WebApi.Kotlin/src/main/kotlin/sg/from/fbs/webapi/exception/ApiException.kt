package sg.from.fbs.webapi.exception

class ApiException(val status: Int, override val message: String) : RuntimeException(message) {
    companion object {
        fun badRequest(message: String) = ApiException(400, message)
        fun unauthorized(message: String = "Unauthorized") = ApiException(401, message)
        fun forbidden(message: String = "Forbidden") = ApiException(403, message)
        fun notFound(message: String = "Not Found") = ApiException(404, message)
    }
}
