package sg.from.fbs.webapi.auth

import jakarta.servlet.http.HttpServletRequest
import jakarta.servlet.http.HttpServletResponse
import org.springframework.stereotype.Component
import org.springframework.web.servlet.HandlerInterceptor

@Component
class AuthInterceptor(private val authCookieService: AuthCookieService) : HandlerInterceptor {
    override fun preHandle(request: HttpServletRequest, response: HttpServletResponse, handler: Any): Boolean {
        authCookieService.getPhone(request)?.let { request.setAttribute(PHONE_ATTR, it) }
        return true
    }

    companion object {
        const val PHONE_ATTR = "Phone"
    }
}
