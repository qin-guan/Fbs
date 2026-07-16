package sg.from.fbs.controllers

import org.springframework.context.ApplicationEventPublisher
import org.springframework.http.HttpStatus
import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.*
import sg.from.fbs.entities.BookingWithUser
import sg.from.fbs.entities.User
import sg.from.fbs.events.BookingDeletedEvent
import sg.from.fbs.events.BookingUpdatedEvent
import sg.from.fbs.repository.BookingRepository
import sg.from.fbs.repository.UserRepository
import java.time.OffsetDateTime
import java.util.UUID

data class AdminPutBookingRequest(
    val startDateTime: OffsetDateTime,
    val endDateTime: OffsetDateTime,
    val conduct: String,
    val description: String?,
    val facilityName: String,
    val pocName: String,
    val pocPhone: String,
    val userPhone: String
)

@RestController
@RequestMapping("/Admin")
class AdminController(
    private val userRepository: UserRepository,
    private val bookingRepository: BookingRepository,
    private val eventPublisher: ApplicationEventPublisher
) {

    @PostMapping("/Users/{targetPhone}/Admin")
    fun toggleAdmin(@PathVariable targetPhone: String, @RequestAttribute("Phone") phone: String): ResponseEntity<Any> {
        val currentUser = userRepository.find { it.phone == phone }
        if (currentUser?.isAdmin != true) {
            throw Exception("You do not have permission to manage admin status.")
        }

        val targetUser = userRepository.find { it.phone == targetPhone } ?: throw Exception("User does not exist.")

        targetUser.isAdmin = !targetUser.isAdmin
        userRepository.update(targetUser)

        return ResponseEntity.ok(targetUser)
    }

    @GetMapping("/Users")
    fun getUsers(@RequestAttribute("Phone") phone: String): ResponseEntity<List<User>> {
        val currentUser = userRepository.find { it.phone == phone }
        if (currentUser?.isAdmin != true) {
            throw Exception("You do not have permission to view users.")
        }

        return ResponseEntity.ok(userRepository.getList())
    }

    @GetMapping("/Bookings")
    fun getBookings(@RequestAttribute("Phone") phone: String): ResponseEntity<List<BookingWithUser>> {
        val currentUser = userRepository.find { it.phone == phone }
        if (currentUser?.isAdmin != true) {
            throw Exception("You do not have permission to view bookings.")
        }

        val users = userRepository.getList()
        val all = bookingRepository.getList()

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

    @GetMapping("/Bookings/{id}")
    fun getBookingById(@PathVariable id: UUID, @RequestAttribute("Phone") phone: String): ResponseEntity<BookingWithUser> {
        val currentUser = userRepository.find { it.phone == phone }
        if (currentUser?.isAdmin != true) {
            throw Exception("You do not have permission to view bookings.")
        }

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

    @PutMapping("/Bookings/{id}")
    fun updateBooking(
        @PathVariable id: UUID,
        @RequestBody req: AdminPutBookingRequest,
        @RequestAttribute("Phone") phone: String
    ): ResponseEntity<BookingWithUser> {
        val currentUser = userRepository.find { it.phone == phone }
        if (currentUser?.isAdmin != true) {
            throw Exception("You do not have permission to edit bookings.")
        }

        val booking = bookingRepository.find { it.id == id } ?: throw Exception("Booking does not exist.")

        booking.startDateTime = req.startDateTime
        booking.endDateTime = req.endDateTime
        booking.conduct = req.conduct
        booking.description = req.description
        booking.facilityName = req.facilityName
        booking.pocName = req.pocName
        booking.pocPhone = req.pocPhone
        booking.userPhone = req.userPhone

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

    @DeleteMapping("/Bookings/{id}")
    fun deleteBooking(@PathVariable id: UUID, @RequestAttribute("Phone") phone: String): ResponseEntity<Any> {
        val currentUser = userRepository.find { it.phone == phone }
        if (currentUser?.isAdmin != true) {
            throw Exception("You do not have permission to delete bookings.")
        }

        val booking = bookingRepository.find { it.id == id } ?: throw Exception("Booking does not exist.")

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
}