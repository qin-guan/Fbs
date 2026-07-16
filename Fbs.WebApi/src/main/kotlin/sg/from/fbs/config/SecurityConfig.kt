package sg.from.fbs.config

import io.jsonwebtoken.Jwts
import jakarta.servlet.FilterChain
import jakarta.servlet.http.Cookie
import jakarta.servlet.http.HttpServletRequest
import jakarta.servlet.http.HttpServletResponse
import org.springframework.context.annotation.Configuration
import org.springframework.stereotype.Component
import org.springframework.web.filter.OncePerRequestFilter
import org.springframework.web.servlet.config.annotation.CorsRegistry
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer
import java.util.*
import javax.crypto.SecretKey
import io.jsonwebtoken.security.Keys

@Configuration
class WebConfig : WebMvcConfigurer {
    override fun addCorsMappings(registry: CorsRegistry) {
        registry.addMapping("/**")
            .allowedOrigins(
                "http://localhost:3000",
                "https://*.asse.devtunnels.ms",
                "https://*.3sib-fbs.pages.dev",
                "https://3sib-fbs.pages.dev",
                "https://*.3sib-fbs.from.sg",
                "https://3sib-fbs.from.sg"
            )
            .allowCredentials(true)
            .allowedHeaders("*")
            .allowedMethods("*")
    }
}

object JwtUtil {
    private val secret = System.getenv("JWT_SECRET") ?: "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0u1v2w3x4y5z6a7b8c9d0e1f2"
    val key: SecretKey = Keys.hmacShaKeyFor(secret.toByteArray())

    fun createToken(phone: String): String {
        return Jwts.builder()
            .claim("Phone", phone)
            .expiration(Date(System.currentTimeMillis() + 86400000L)) // 1 day
            .signWith(key)
            .compact()
    }

    fun parseToken(token: String): String? {
        return try {
            val claims = Jwts.parser()
                .verifyWith(key)
                .build()
                .parseSignedClaims(token)
            claims.payload["Phone"] as String?
        } catch (e: Exception) {
            null
        }
    }
}

@Component
class AuthFilter : OncePerRequestFilter() {
    override fun doFilterInternal(
        request: HttpServletRequest,
        response: HttpServletResponse,
        filterChain: FilterChain
    ) {
        val publicEndpoints = listOf(
            "/Auth/Login",
            "/Auth/Verify",
            "/Bot",
            "/Cache/Purge",
            "/Admin/Users/" // Has its own anonymous allow in FastEndpoints for post
        )

        val isPublic = publicEndpoints.any { request.requestURI.startsWith(it) }

        var phone: String? = null
        val cookies = request.cookies
        if (cookies != null) {
            for (cookie in cookies) {
                if (cookie.name == "auth_cookie") {
                    phone = JwtUtil.parseToken(cookie.value)
                    break
                }
            }
        }

        if (phone != null) {
            request.setAttribute("Phone", phone)
        } else if (!isPublic && request.method != "OPTIONS") {
            response.sendError(HttpServletResponse.SC_UNAUTHORIZED)
            return
        }

        filterChain.doFilter(request, response)
    }
}