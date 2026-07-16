package sg.from.fbs.webapi.auth

import jakarta.servlet.http.HttpServletRequest
import org.springframework.stereotype.Component
import sg.from.fbs.webapi.exception.ApiException

@Component
class AuthContext(private val request: HttpServletRequest) {
    fun phoneOrNull(): String? = request.getAttribute(AuthInterceptor.PHONE_ATTR) as? String

    fun phone(): String = phoneOrNull() ?: throw ApiException.unauthorized("User does not exist")
}
