package sg.from.fbs.entities

import java.time.OffsetDateTime
import java.util.UUID

data class BookingWithUser(
    var id: UUID = UUID.randomUUID(),
    var facilityName: String? = null,
    var conduct: String? = null,
    var description: String? = null,
    var pocName: String? = null,
    var pocPhone: String? = null,
    var startDateTime: OffsetDateTime? = null,
    var endDateTime: OffsetDateTime? = null,
    var user: User? = null
)