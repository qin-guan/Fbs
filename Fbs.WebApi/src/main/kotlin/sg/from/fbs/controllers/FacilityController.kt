package sg.from.fbs.controllers

import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RequestParam
import org.springframework.web.bind.annotation.PathVariable
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.entities.Facility
import sg.from.fbs.repository.BookingRepository
import sg.from.fbs.repository.FacilityRepository
import java.time.OffsetDateTime

data class TimeSlot(
    val startDateTime: OffsetDateTime,
    val endDateTime: OffsetDateTime
)

@RestController
@RequestMapping("/Facility")
class FacilityController(
    private val facilityRepository: FacilityRepository,
    private val bookingRepository: BookingRepository
) {

    @GetMapping
    fun getFacilities(): ResponseEntity<List<Facility>> {
        return ResponseEntity.ok(facilityRepository.getList())
    }

    @GetMapping("/{facilityName}/TimeSlots")
    fun getTimeSlots(
        @PathVariable facilityName: String,
        @RequestParam startDateTime: OffsetDateTime,
        @RequestParam endDateTime: OffsetDateTime
    ): ResponseEntity<List<TimeSlot>> {
        val facility = facilityRepository.find { it.name == facilityName }
        if (facility == null) {
            return ResponseEntity.notFound().build()
        }

        val allBookings = bookingRepository.getList()
        val bookings = allBookings.filter {
            it.facilityName == facilityName &&
            it.startDateTime != null && it.endDateTime != null &&
            it.startDateTime!! >= startDateTime &&
            it.endDateTime!! <= endDateTime
        }.sortedBy { it.startDateTime }

        val availabilities = mutableListOf<TimeSlot>()
        var start = startDateTime

        for (booking in bookings) {
            if (booking.startDateTime!! > start) {
                availabilities.add(TimeSlot(start, booking.startDateTime!!))
            }
            start = booking.endDateTime!!
        }

        if (start < endDateTime) {
            availabilities.add(TimeSlot(start, endDateTime))
        }

        return ResponseEntity.ok(availabilities)
    }
}