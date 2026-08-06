package sg.from.fbs.controllers

import jakarta.servlet.http.Cookie
import jakarta.servlet.http.HttpServletResponse
import org.bouncycastle.crypto.generators.SCrypt
import org.springframework.http.HttpStatus
import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.*
import org.springframework.web.client.RestTemplate
import sg.from.fbs.config.JwtUtil
import sg.from.fbs.config.TelegramProperties
import sg.from.fbs.entities.Otp
import sg.from.fbs.entities.User
import sg.from.fbs.repository.OtpRepository
import sg.from.fbs.repository.UserRepository
import java.security.MessageDigest
import java.time.OffsetDateTime
import java.util.*
import kotlin.random.Random

data class LoginRequest(val phone: String)
data class VerifyRequest(val phone: String, val code: String)

@RestController
@RequestMapping("/Auth")
class AuthController(
    private val userRepository: UserRepository,
    private val otpRepository: OtpRepository,
    private val telegramProperties: TelegramProperties
) {
    private val restTemplate = RestTemplate()

    @PostMapping("/Login")
    fun login(@RequestBody req: LoginRequest): ResponseEntity<Any> {
        val user = userRepository.find { it.phone == req.phone }

        if (user == null) {
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body(mapOf(
                "type" to "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "title" to "One or more validation errors occurred.",
                "status" to 400,
                "errors" to mapOf("Phone" to listOf("User is not allow-listed.")),
                "errorCode" to "EX01"
            ))
        }

        if (user.telegramChatId.isNullOrEmpty()) {
            return ResponseEntity.status(HttpStatus.BAD_REQUEST).body(mapOf(
                "type" to "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "title" to "One or more validation errors occurred.",
                "status" to 400,
                "errors" to mapOf("Phone" to listOf("User is not registered on Telegram.")),
                "errorCode" to "EX02"
            ))
        }

        val code = (1..6).map { Random.nextInt(0, 10) }.joinToString("")
        val hashBytes = SCrypt.generate(code.toByteArray(), req.phone.toByteArray(), 16384, 8, 1, 64)
        val hash = hashBytes.joinToString("") { "%02x".format(it) }.uppercase()

        val existingOtp = otpRepository.find { it.phone == req.phone }
        if (existingOtp != null && existingOtp.createdAt!!.plusSeconds(60).isAfter(OffsetDateTime.now())) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build()
        }

        if (existingOtp != null) {
            existingOtp.code = hash
            existingOtp.createdAt = OffsetDateTime.now()
            otpRepository.update(existingOtp)
        } else {
            otpRepository.insert(Otp(phone = req.phone, code = hash, createdAt = OffsetDateTime.now()))
        }

        try {
            restTemplate.postForObject(
                "https://api.telegram.org/bot${telegramProperties.token}/sendMessage",
                mapOf(
                    "chat_id" to user.telegramChatId,
                    "text" to "Your login OTP is $code"
                ),
                String::class.java
            )
        } catch (e: Exception) {
            e.printStackTrace()
        }

        return ResponseEntity.ok().build()
    }

    @PostMapping("/Verify")
    fun verify(@RequestBody req: VerifyRequest, response: HttpServletResponse): ResponseEntity<Any> {
        val otp = otpRepository.find { it.phone == req.phone }
        if (otp?.code == null) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build()
        }

        val hashBytes = SCrypt.generate(req.code.toByteArray(), req.phone.toByteArray(), 16384, 8, 1, 64)
        val hash = hashBytes.joinToString("") { "%02x".format(it) }.uppercase()

        if (!MessageDigest.isEqual(otp.code!!.toByteArray(), hash.toByteArray())) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build()
        }

        val user = userRepository.find { it.phone == req.phone }
        if (user?.phone == null) {
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build()
        }

        otpRepository.delete { it.phone == req.phone }

        val token = JwtUtil.createToken(user.phone!!)
        val cookie = Cookie("auth_cookie", token).apply {
            maxAge = 86400 // 1 day
            path = "/"
            isHttpOnly = true
            // In FastEndpoints, secure policy and domain were conditional on environment.
        }
        response.addCookie(cookie)

        return ResponseEntity.ok().build()
    }

    @PostMapping("/Logout")
    fun logout(response: HttpServletResponse): ResponseEntity<Any> {
        val cookie = Cookie("auth_cookie", "").apply {
            maxAge = 0
            path = "/"
        }
        response.addCookie(cookie)
        return ResponseEntity.ok().build()
    }

    @GetMapping("/Me")
    fun me(@RequestAttribute("Phone") phone: String): ResponseEntity<User> {
        val user = userRepository.get { it.phone == phone }
        return ResponseEntity.ok(user)
    }
}