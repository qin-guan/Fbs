package sg.from.fbs.controllers

import org.springframework.context.ApplicationEventPublisher
import org.springframework.http.HttpStatus
import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.*
import sg.from.fbs.entities.Booking
import sg.from.fbs.entities.BookingWithUser
import sg.from.fbs.events.BookingCreatedEvent
import sg.from.fbs.events.BookingDeletedEvent
import sg.from.fbs.events.BookingUpdatedEvent
import sg.from.fbs.repository.BookingRepository
import sg.from.fbs.repository.FacilityRepository
import sg.from.fbs.repository.UserRepository
import java.time.OffsetDateTime
import java.util.UUID

data class BookingPostRequest(
    val facilityName: String,
    val conduct: String,
    val description: String?,
    val pocName: String,
    val pocPhone: String,
    val startDateTime: OffsetDateTime,
    val endDateTime: OffsetDateTime
)

data class BookingPutRequest(
    val conduct: String,
    val description: String?,
    val pocName: String,
    val pocPhone: String,
    val startDateTime: OffsetDateTime,
    val endDateTime: OffsetDateTime
)

@RestController
@RequestMapping("/Booking")
class BookingController(
    private val bookingRepository: BookingRepository,
    private val userRepository: UserRepository,
    private val facilityRepository: FacilityRepository,
    private val eventPublisher: ApplicationEventPublisher
) {

    @GetMapping
    fun getBookings(
        @RequestParam(required = false) userPhone: String?,
        @RequestParam(required = false) startsAfter: OffsetDateTime?
    ): ResponseEntity<List<BookingWithUser>> {
        var all = bookingRepository.getList()

        if (userPhone != null) {
            all = all.filter { it.userPhone == userPhone }
        }

        if (startsAfter != null) {
            all = all.filter { it.startDateTime != null && it.startDateTime!! >= startsAfter }
        }

        val users = userRepository.getList()
        val withUser = all.map { booking ->
            BookingWithUser(
                id = booking.id,
                facilityName = booking.facilityName,
                conduct = booking.conduct,
                description = booking.description,
                pocName = booking.pocName,
                pocPhone = booking.pocPhone,
                startDateTime = booking.startDateTime,
                endDateTime = booking.endDateTime,
                user = users.single { it.phone == booking.userPhone }
            )
        }

        return ResponseEntity.ok(withUser.sortedByDescending { it.startDateTime })
    }

    @GetMapping("/{id}")
    fun getBookingById(@PathVariable id: UUID): ResponseEntity<BookingWithUser> {
        val booking = bookingRepository.find { it.id == id } ?: return ResponseEntity.notFound().build()
        val users = userRepository.getList()

        val withUser = BookingWithUser(
            id = booking.id,
            facilityName = booking.facilityName,
            conduct = booking.conduct,
            description = booking.description,
            pocName = booking.pocName,
            pocPhone = booking.pocPhone,
            startDateTime = booking.startDateTime,
            endDateTime = booking.endDateTime,
            user = users.single { it.phone == booking.userPhone }
        )

        return ResponseEntity.ok(withUser)
    }

    @PostMapping
    fun createBooking(@RequestBody req: BookingPostRequest, @RequestAttribute("Phone") phone: String): ResponseEntity<Any> {
        // Validation
        if (req.startDateTime < OffsetDateTime.now()) {
            return validationError("StartDateTime", "Start Date Time must be in the future")
        }
        if (req.endDateTime <= req.startDateTime) {
            return validationError("EndDateTime", "End time must be after start time")
        }
        if (req.startDateTime.minute % 30 != 0) {
            return validationError("StartDateTime", "Duration must be in 30 minute intervals")
        }
        if (req.endDateTime.minute % 30 != 0) {
            return validationError("EndDateTime", "Duration must be in 30 minute intervals")
        }
        if (req.conduct.isEmpty() || req.conduct.length > 100) {
            return validationError("Conduct", "Conduct must be between 1 and 100 characters")
        }

        val facility = facilityRepository.find { it.name == req.facilityName }
        if (facility?.scope == null) {
            return ResponseEntity.notFound().build()
        }

        val user = userRepository.get { it.phone == phone }
        if (user.unit == null) {
            throw Exception("User must have unit specified in order to book facilities.")
        }

        if (!facility.availableForAll && !facility.scope!!.contains(user.unit)) {
            return validationError("FacilityName", "You do not have permission to book this facility")
        }

        val bookings = bookingRepository.getList()
        val overlapping = bookings.firstOrNull {
            it.facilityName == facility.name &&
            it.startDateTime != null && it.endDateTime != null &&
            it.startDateTime!! < req.endDateTime &&
            it.endDateTime!! > req.startDateTime
        }

        if (overlapping != null) {
            return validationError("EndDateTime", "Overlaps with booking ${overlapping.id}")
        }

        val booking = Booking(
            startDateTime = req.startDateTime,
            endDateTime = req.endDateTime,
            conduct = req.conduct,
            description = req.description,
            facilityName = req.facilityName,
            pocName = req.pocName,
            pocPhone = req.pocPhone,
            userPhone = phone
        )

        bookingRepository.insert(booking)

        eventPublisher.publishEvent(BookingCreatedEvent(
            id = booking.id,
            facilityName = booking.facilityName,
            conduct = booking.conduct,
            description = booking.description,
            pocName = booking.pocName,
            pocPhone = booking.pocPhone,
            startDateTime = booking.startDateTime,
            endDateTime = booking.endDateTime,
            userPhone = booking.userPhone
        ))

        return ResponseEntity.status(HttpStatus.CREATED).body(booking)
    }

    @PostMapping("/{id}")
    fun updateBooking(
        @PathVariable id: UUID,
        @RequestBody req: BookingPutRequest,
        @RequestAttribute("Phone") phone: String
    ): ResponseEntity<Any> {
        // Validation
        if (req.conduct.isEmpty() || req.conduct.length > 100) {
            return validationError("Conduct", "Conduct must be between 1 and 100 characters")
        }

        val booking = bookingRepository.find { it.id == id } ?: return ResponseEntity.notFound().build()

        if (booking.userPhone != phone) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN).build()
        }

        val facility = facilityRepository.get { it.name == booking.facilityName }

        val bookings = bookingRepository.getList()
        val overlapping = bookings.firstOrNull {
            it.id != booking.id &&
            it.facilityName == facility.name &&
            it.startDateTime != null && it.endDateTime != null &&
            it.startDateTime!! < req.endDateTime &&
            it.endDateTime!! > req.startDateTime
        }

        if (overlapping != null) {
            return validationError("EndDateTime", "Overlaps with booking ${overlapping.id}")
        }

        booking.startDateTime = req.startDateTime
        booking.endDateTime = req.endDateTime
        booking.conduct = req.conduct
        booking.description = req.description
        booking.pocName = req.pocName
        booking.pocPhone = req.pocPhone

        bookingRepository.update(booking)

        eventPublisher.publishEvent(BookingUpdatedEvent(
            id = booking.id,
            facilityName = booking.facilityName,
            conduct = booking.conduct,
            description = booking.description,
            pocName = booking.pocName,
            pocPhone = booking.pocPhone,
            startDateTime = booking.startDateTime,
            endDateTime = booking.endDateTime,
            userPhone = booking.userPhone
        ))

        return ResponseEntity.ok(booking)
    }

    @DeleteMapping("/{id}")
    fun deleteBooking(@PathVariable id: UUID, @RequestAttribute("Phone") phone: String): ResponseEntity<Any> {
        val booking = bookingRepository.find { it.id == id } ?: return ResponseEntity.notFound().build()

        if (booking.userPhone != phone) {
            return ResponseEntity.status(HttpStatus.FORBIDDEN).build()
        }

        bookingRepository.delete { it.id == booking.id }

        eventPublisher.publishEvent(BookingDeletedEvent(
            id = booking.id,
            facilityName = booking.facilityName,
            conduct = booking.conduct,
            description = booking.description,
            pocName = booking.pocName,
            pocPhone = booking.pocPhone,
            startDateTime = booking.startDateTime,
            endDateTime = booking.endDateTime,
            userPhone = booking.userPhone
        ))

        return ResponseEntity.noContent().build()
    }

    private fun validationError(field: String, message: String): ResponseEntity<Any> {
        return ResponseEntity.status(HttpStatus.BAD_REQUEST).body(mapOf(
            "type" to "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "title" to "One or more validation errors occurred.",
            "status" to 400,
            "errors" to mapOf(field to listOf(message)),
            "errorCode" to "EX"
        ))
    }
}