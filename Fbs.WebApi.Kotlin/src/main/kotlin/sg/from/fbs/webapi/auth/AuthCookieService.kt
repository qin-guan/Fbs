package sg.from.fbs.webapi.auth

import jakarta.servlet.http.Cookie
import jakarta.servlet.http.HttpServletRequest
import jakarta.servlet.http.HttpServletResponse
import org.springframework.stereotype.Service
import sg.from.fbs.webapi.config.AuthCookieProperties
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.time.Instant
import java.util.Base64
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

@Service
class AuthCookieService(private val props: AuthCookieProperties) {
    fun signIn(phone: String, response: HttpServletResponse) {
        val expiresAt = Instant.now().plusSeconds(props.maxAgeDays * 86400)
        val payload = "$phone|${expiresAt.epochSecond}"
        val signature = sign(payload)
        val value = Base64.getUrlEncoder().withoutPadding().encodeToString("$payload|$signature".toByteArray(StandardCharsets.UTF_8))
        response.addCookie(cookie(value, props.maxAgeDays * 86400))
    }

    fun signOut(response: HttpServletResponse) {
        response.addCookie(cookie("", 0))
    }

    fun getPhone(request: HttpServletRequest): String? {
        val raw = request.cookies?.firstOrNull { it.name == props.name }?.value ?: return null
        val decoded = runCatching {
            String(Base64.getUrlDecoder().decode(raw), StandardCharsets.UTF_8)
        }.getOrNull() ?: return null
        val parts = decoded.split("|")
        if (parts.size != 3) return null
        val payload = "${parts[0]}|${parts[1]}"
        if (!constantTimeEquals(parts[2], sign(payload))) return null
        if (Instant.now().epochSecond >= parts[1].toLongOrNull() ?: return null) return null
        return parts[0]
    }

    private fun sign(payload: String): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(props.secret.toByteArray(StandardCharsets.UTF_8), "HmacSHA256"))
        return Base64.getUrlEncoder().withoutPadding().encodeToString(mac.doFinal(payload.toByteArray(StandardCharsets.UTF_8)))
    }

    private fun constantTimeEquals(a: String, b: String): Boolean =
        MessageDigest.isEqual(a.toByteArray(StandardCharsets.UTF_8), b.toByteArray(StandardCharsets.UTF_8))

    private fun cookie(value: String, maxAge: Long): Cookie {
        val cookie = Cookie(props.name, value)
        cookie.path = "/"
        cookie.isHttpOnly = true
        cookie.secure = props.secure
        cookie.maxAge = maxAge.toInt()
        if (!props.domain.isNullOrBlank()) cookie.domain = props.domain
        return cookie
    }
}
