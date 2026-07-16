package sg.from.fbs.webapi.dto

import sg.from.fbs.webapi.entity.UserEntity
import java.time.OffsetDateTime
import java.util.UUID

data class FacilityDto(
    val name: String?,
    val group: String?,
    val scope: List<String>?,
)

data class BookingWithUserDto(
    val id: UUID,
    val facilityName: String?,
    val conduct: String?,
    val description: String?,
    val pocName: String?,
    val pocPhone: String?,
    val startDateTime: OffsetDateTime?,
    val endDateTime: OffsetDateTime?,
    val user: UserEntity?,
)

data class TimeSlotDto(
    val startDateTime: OffsetDateTime,
    val endDateTime: OffsetDateTime,
    val booking: BookingWithUserDto?,
)
