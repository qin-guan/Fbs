package sg.from.fbs.webapi.entity

import com.fasterxml.jackson.annotation.JsonProperty
import java.time.OffsetDateTime
import java.util.UUID

open class Entity(open var row: Int = 0)

data class OtpEntity(
    override var row: Int = 0,
    var phone: String? = null,
    var code: String? = null,
    var createdAt: OffsetDateTime? = null,
) : Entity(row)

data class UserEntity(
    override var row: Int = 0,
    var unit: String? = null,
    var name: String? = null,
    var phone: String? = null,
    var telegramChatId: String? = null,
    var notificationGroup: String? = null,
    @field:JsonProperty("isAdmin")
    var isAdmin: Boolean = false,
) : Entity(row)

data class FacilityEntity(
    override var row: Int = 0,
    var name: String? = null,
    var group: String? = null,
    var scope: List<String>? = null,
) : Entity(row) {
    fun availableForAll() = scope?.contains("All") == true
}

data class NominalRollEntity(
    override var row: Int = 0,
    var name: String? = null,
    var unit: String? = null,
    var phone: String? = null,
) : Entity(row)

data class BookingEntity(
    override var row: Int = 0,
    var id: UUID = UUID.randomUUID(),
    var startDateTime: OffsetDateTime? = null,
    var endDateTime: OffsetDateTime? = null,
    var conduct: String? = null,
    var description: String? = null,
    var pocName: String? = null,
    var pocPhone: String? = null,
    var facilityName: String? = null,
    var userPhone: String? = null,
) : Entity(row)
