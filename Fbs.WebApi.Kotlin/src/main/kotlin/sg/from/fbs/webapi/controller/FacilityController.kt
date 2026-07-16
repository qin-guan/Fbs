package sg.from.fbs.webapi.controller

import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.PathVariable
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RequestParam
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.webapi.auth.AuthContext
import sg.from.fbs.webapi.dto.BookingWithUserDto
import sg.from.fbs.webapi.dto.FacilityDto
import sg.from.fbs.webapi.dto.TimeSlotDto
import sg.from.fbs.webapi.exception.ApiException
import sg.from.fbs.webapi.repository.BookingRepository
import sg.from.fbs.webapi.repository.FacilityRepository
import sg.from.fbs.webapi.repository.UserRepository
import java.time.OffsetDateTime

@RestController
class FacilityController(
    private val auth: AuthContext,
    private val facilityRepository: FacilityRepository,
    private val userRepository: UserRepository,
    private val bookingRepository: BookingRepository,
) {
    @GetMapping("/Facility")
    fun facilities(): List<FacilityDto> {
        val user = userRepository.getByPhone(auth.phone())
        val unit = user.unit ?: throw ApiException.badRequest("User needs to have a unit in order to retrieve facility list.")
        return facilityRepository.getList()
            .filter { it.scope != null }
            .filter { it.availableForAll() || it.scope!!.contains(unit) }
            .map { FacilityDto(it.name, it.group, it.scope) }
    }

    @GetMapping("/Facility/{name}/TimeSlots")
    fun timeSlots(
        @PathVariable("name") name: String,
        @RequestParam startTime: OffsetDateTime,
        @RequestParam endTime: OffsetDateTime,
    ): List<TimeSlotDto> {
        if (startTime >= endTime || startTime.minute % 30 != 0 || endTime.minute % 30 != 0) {
            throw ApiException.badRequest("Either the start time is greater than end time, or the time values provided are not in the correct intervals.")
        }

        val facility = facilityRepository.getByName(name)
        val users = userRepository.getList().associateBy { it.phone }
        val overlapping = bookingRepository.getList()
            .filter { it.facilityName == facility.name }
            .filter { it.startDateTime != null && it.endDateTime != null && it.startDateTime!! <= endTime && it.endDateTime!! >= startTime }
            .map {
                BookingWithUserDto(
                    id = it.id,
                    facilityName = it.facilityName,
                    conduct = it.conduct,
                    description = it.description,
                    pocName = it.pocName,
                    pocPhone = it.pocPhone,
                    startDateTime = it.startDateTime,
                    endDateTime = it.endDateTime,
                    user = users[it.userPhone],
                )
            }

        val slots = mutableListOf<TimeSlotDto>()
        var current = startTime
        while (current < endTime) {
            val next = current.plusMinutes(30)
            val overlap = overlapping.singleOrNull {
                it.startDateTime != null && it.endDateTime != null && it.startDateTime < next && it.endDateTime > current
            }
            slots += TimeSlotDto(current, next, overlap)
            current = next
        }
        return slots
    }
}
