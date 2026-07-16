package sg.from.fbs.webapi.controller

import jakarta.servlet.http.HttpServletResponse
import jakarta.validation.Valid
import jakarta.validation.constraints.NotBlank
import org.bouncycastle.crypto.generators.SCrypt
import org.springframework.http.HttpStatus
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.RequestBody
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.webapi.auth.AuthContext
import sg.from.fbs.webapi.auth.AuthCookieService
import sg.from.fbs.webapi.entity.OtpEntity
import sg.from.fbs.webapi.entity.UserEntity
import sg.from.fbs.webapi.exception.ApiException
import sg.from.fbs.webapi.integration.TelegramClient
import sg.from.fbs.webapi.repository.OtpRepository
import sg.from.fbs.webapi.repository.UserRepository
import java.security.MessageDigest
import java.security.SecureRandom
import java.time.OffsetDateTime
import kotlin.text.Charsets.UTF_8

@RestController
@RequestMapping("/Auth")
class AuthController(
    private val auth: AuthContext,
    private val cookieService: AuthCookieService,
    private val otpRepository: OtpRepository,
    private val userRepository: UserRepository,
    private val telegramClient: TelegramClient,
) {
    @PostMapping("/Login")
    @Suppress("LongMethod")
    fun login(@Valid @RequestBody req: LoginRequest) {
        val user = userRepository.findByPhone(req.phone)
        when {
            user == null -> throw ApiException.badRequest("User is not allow-listed.")
            user.telegramChatId.isNullOrBlank() -> throw ApiException.badRequest("User is not registered on Telegram.")
        }

        val existing = otpRepository.findByPhone(req.phone)
        if (existing?.createdAt?.plusSeconds(60)?.isAfter(OffsetDateTime.now()) == true) {
            throw ApiException(HttpStatus.UNAUTHORIZED.value(), "Unauthorized")
        }

        val code = (1..6).map { SecureRandom().nextInt(10) }.joinToString("")
        val hash = scrypt(code, req.phone)

        if (existing != null) {
            existing.code = hash
            existing.createdAt = OffsetDateTime.now()
            otpRepository.update(existing)
        } else {
            otpRepository.insert(OtpEntity(phone = req.phone, code = hash, createdAt = OffsetDateTime.now()))
        }

        telegramClient.sendMessage(user.telegramChatId!!, "Your login OTP is $code")
    }

    @PostMapping("/Verify")
    fun verify(@Valid @RequestBody req: VerifyRequest, response: HttpServletResponse) {
        val otp = otpRepository.findByPhone(req.phone)?.code ?: throw ApiException.unauthorized()
        val expected = otp.hexToBytes()
        val actual = scrypt(req.code, req.phone).hexToBytes()
        if (!MessageDigest.isEqual(expected, actual)) throw ApiException.unauthorized()

        val user = userRepository.findByPhone(req.phone) ?: throw ApiException.unauthorized()
        otpRepository.deleteByPhone(req.phone)
        cookieService.signIn(user.phone ?: throw ApiException.unauthorized(), response)
    }

    @PostMapping("/Logout")
    fun logout(response: HttpServletResponse) {
        cookieService.signOut(response)
    }

    @GetMapping("/Me")
    fun me(): UserEntity = userRepository.getByPhone(auth.phone())

    private fun scrypt(password: String, salt: String): String =
        SCrypt.generate(password.toByteArray(UTF_8), salt.toByteArray(UTF_8), 16384, 8, 1, 64).toHex()

    private fun ByteArray.toHex(): String = joinToString("") { "%02X".format(it) }

    private fun String.hexToBytes(): ByteArray = chunked(2).map { it.toInt(16).toByte() }.toByteArray()
}

data class LoginRequest(@field:NotBlank val phone: String)

data class VerifyRequest(
    @field:NotBlank val phone: String,
    @field:NotBlank val code: String,
)
