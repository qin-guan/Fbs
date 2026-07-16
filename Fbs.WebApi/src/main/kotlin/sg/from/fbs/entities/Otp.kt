package sg.from.fbs.entities

import java.time.OffsetDateTime

data class Otp(
    var row: Int = 0,
    var phone: String? = null,
    var code: String? = null,
    var createdAt: OffsetDateTime? = null
)