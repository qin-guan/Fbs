package sg.from.fbs.webapi.controller

import jakarta.validation.Valid
import jakarta.validation.constraints.Size
import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.DeleteMapping
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.PathVariable
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.RequestBody
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RequestParam
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.webapi.auth.AuthContext
import sg.from.fbs.webapi.dto.BookingWithUserDto
import sg.from.fbs.webapi.entity.BookingEntity
import sg.from.fbs.webapi.event.BookingEvent
import sg.from.fbs.webapi.exception.ApiException
import sg.from.fbs.webapi.repository.BookingRepository
import sg.from.fbs.webapi.repository.FacilityRepository
import sg.from.fbs.webapi.repository.UserRepository
import sg.from.fbs.webapi.service.BookingNotificationService
import java.net.URI
import java.time.OffsetDateTime
import java.util.UUID

@RestController
@RequestMapping("/Booking")
class BookingController(
    private val auth: AuthContext,
    private val bookingRepository: BookingRepository,
    private val userRepository: UserRepository,
    private val facilityRepository: FacilityRepository,
    private val notifications: BookingNotificationService,
) {
    @GetMapping
    fun list(
        @RequestParam(required = false) userPhone: String?,
        @RequestParam(required = false) startsAfter: OffsetDateTime?,
    ): List<BookingWithUserDto> {
        auth.phone()
        var all = bookingRepository.getList()
        if (userPhone != null) all = all.filter { it.userPhone == userPhone }
        if (startsAfter != null) all = all.filter { it.startDateTime != null && it.startDateTime!! >= startsAfter }
        val users = userRepository.getList()
        return all.map {
            BookingWithUserDto(
                id = it.id,
                facilityName = it.facilityName,
                conduct = it.conduct,
                description = it.description,
                pocName = it.pocName,
                pocPhone = it.pocPhone,
                startDateTime = it.startDateTime,
                endDateTime = it.endDateTime,
                user = users.single { u -> u.phone == it.userPhone },
            )
        }.sortedByDescending { it.startDateTime }
    }

    @PostMapping
    fun create(@Valid @RequestBody req: BookingCreateRequest): ResponseEntity<BookingEntity> {
        val phone = auth.phone()
        validateBookingTimes(req.startDateTime, req.endDateTime)

        val facility = facilityRepository.findByName(req.facilityName ?: "")
            ?: throw ApiException.notFound("Not Found")

        val user = userRepository.getByPhone(phone)
        val unit = user.unit ?: throw ApiException.badRequest("User must have unit specified in order to book facilities.")
        if (!facility.availableForAll() && !facility.scope.orEmpty().contains(unit)) {
            throw ApiException.badRequest("You do not have permission to book this facility")
        }

        val overlap = bookingRepository.getList().firstOrNull {
            it.facilityName == facility.name && it.startDateTime!! < req.endDateTime && it.endDateTime!! > req.startDateTime
        }
        if (overlap != null) throw ApiException.badRequest("Overlaps with booking ${overlap.id}")

        val booking = bookingRepository.insert(
            BookingEntity(
                startDateTime = req.startDateTime,
                endDateTime = req.endDateTime,
                conduct = req.conduct,
                description = req.description,
                facilityName = req.facilityName,
                pocName = req.pocName,
                pocPhone = req.pocPhone,
                userPhone = phone,
            ),
        )
        notifications.created(booking.toEvent())
        return ResponseEntity.created(URI.create("/Booking/${booking.id}")).body(booking)
    }

    @GetMapping("/{id}")
    fun byId(@PathVariable id: UUID): BookingWithUserDto {
        auth.phone()
        val booking = bookingRepository.findById(id) ?: throw ApiException.notFound()
        return BookingWithUserDto(
            id = booking.id,
            facilityName = booking.facilityName,
            conduct = booking.conduct,
            description = booking.description,
            pocName = booking.pocName,
            pocPhone = booking.pocPhone,
            startDateTime = booking.startDateTime,
            endDateTime = booking.endDateTime,
            user = userRepository.getByPhone(booking.userPhone),
        )
    }

    @PostMapping("/{id}")
    fun updateOwn(@PathVariable id: UUID, @Valid @RequestBody req: BookingUpdateRequest): ResponseEntity<BookingEntity> {
        val phone = auth.phone()
        val booking = bookingRepository.findById(id) ?: throw ApiException.badRequest("Booking does not exist.")
        val creator = userRepository.findByPhone(booking.userPhone)
        val current = userRepository.findByPhone(phone)
        if (creator?.unit != current?.unit) throw ApiException.forbidden("You are not allowed to update this booking.")

        booking.conduct = req.conduct
        booking.description = req.description
        booking.pocName = req.pocName
        booking.pocPhone = req.pocPhone
        booking.userPhone = phone
        val updated = bookingRepository.update(booking)
        notifications.updated(updated.toEvent())
        return ResponseEntity.created(URI.create("/Booking/${updated.id}")).body(updated)
    }

    @DeleteMapping("/{id}")
    fun deleteOwn(@PathVariable id: UUID): ResponseEntity<Void> {
        val phone = auth.phone()
        val booking = bookingRepository.findById(id) ?: throw ApiException.notFound()
        if (phone != booking.userPhone) throw ApiException.forbidden()
        bookingRepository.delete(booking.id)
        notifications.deleted(booking.toEvent())
        return ResponseEntity.noContent().build()
    }

    private fun validateBookingTimes(start: OffsetDateTime, end: OffsetDateTime) {
        if (start.isBefore(OffsetDateTime.now())) throw ApiException.badRequest("Start Date Time must be in the future")
        if (end <= start) throw ApiException.badRequest("End time must be after start time")
        if (start.minute % 30 != 0 || end.minute % 30 != 0) throw ApiException.badRequest("Duration must be in 30 minute intervals")
    }

    private fun BookingEntity.toEvent() = BookingEvent(id, facilityName, conduct, description, pocName, pocPhone, startDateTime, endDateTime, userPhone)
}

data class BookingCreateRequest(
    @field:Size(max = 100)
    val conduct: String?,
    val description: String?,
    val facilityName: String?,
    val pocName: String?,
    val pocPhone: String?,
    val startDateTime: OffsetDateTime,
    val endDateTime: OffsetDateTime,
)

data class BookingUpdateRequest(
    @field:Size(max = 100)
    val conduct: String?,
    val description: String?,
    val pocName: String?,
    val pocPhone: String?,
)
