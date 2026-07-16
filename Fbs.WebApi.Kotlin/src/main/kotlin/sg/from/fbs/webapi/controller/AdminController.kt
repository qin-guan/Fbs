package sg.from.fbs.webapi.controller

import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.DeleteMapping
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.PathVariable
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.PutMapping
import org.springframework.web.bind.annotation.RequestBody
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.webapi.auth.AuthContext
import sg.from.fbs.webapi.dto.BookingWithUserDto
import sg.from.fbs.webapi.entity.BookingEntity
import sg.from.fbs.webapi.entity.UserEntity
import sg.from.fbs.webapi.event.BookingEvent
import sg.from.fbs.webapi.exception.ApiException
import sg.from.fbs.webapi.repository.BookingRepository
import sg.from.fbs.webapi.repository.UserRepository
import sg.from.fbs.webapi.service.BookingNotificationService
import java.util.UUID

@RestController
@RequestMapping("/Admin")
class AdminController(
    private val auth: AuthContext,
    private val userRepository: UserRepository,
    private val bookingRepository: BookingRepository,
    private val notifications: BookingNotificationService,
) {
    @GetMapping("/Bookings")
    fun bookings(): List<BookingWithUserDto> {
        requireAdmin()
        val users = userRepository.getList()
        return bookingRepository.getList().map {
            BookingWithUserDto(it.id, it.facilityName, it.conduct, it.description, it.pocName, it.pocPhone, it.startDateTime, it.endDateTime, users.firstOrNull { u -> u.phone == it.userPhone })
        }.sortedByDescending { it.startDateTime }
    }

    @GetMapping("/Bookings/{id}")
    fun bookingById(@PathVariable id: UUID): BookingWithUserDto {
        requireAdmin()
        val booking = bookingRepository.findById(id) ?: throw ApiException.notFound()
        return BookingWithUserDto(booking.id, booking.facilityName, booking.conduct, booking.description, booking.pocName, booking.pocPhone, booking.startDateTime, booking.endDateTime, userRepository.findByPhone(booking.userPhone))
    }

    @PutMapping("/Bookings/{id}")
    fun updateBooking(@PathVariable id: UUID, @RequestBody req: BookingUpdateRequest): BookingEntity {
        requireAdmin()
        val booking = bookingRepository.findById(id) ?: throw ApiException.badRequest("Booking does not exist.")
        booking.conduct = req.conduct
        booking.description = req.description
        booking.pocName = req.pocName
        booking.pocPhone = req.pocPhone
        val updated = bookingRepository.update(booking)
        notifications.updated(updated.toEvent())
        return updated
    }

    @DeleteMapping("/Bookings/{id}")
    fun deleteBooking(@PathVariable id: UUID): ResponseEntity<Void> {
        requireAdmin()
        val booking = bookingRepository.findById(id) ?: throw ApiException.badRequest("Booking does not exist.")
        bookingRepository.delete(booking.id)
        notifications.deleted(booking.toEvent())
        return ResponseEntity.noContent().build()
    }

    @GetMapping("/Users")
    fun users(): List<UserEntity> {
        requireAdmin()
        return userRepository.getList()
    }

    @PostMapping("/Users/{phone}/Admin")
    fun toggleAdmin(@PathVariable phone: String): UserEntity {
        requireAdmin()
        val target = userRepository.findByPhone(phone) ?: throw ApiException.badRequest("User does not exist.")
        target.isAdmin = !target.isAdmin
        return userRepository.update(target)
    }

    private fun requireAdmin() {
        val current = userRepository.findByPhone(auth.phone())
        if (current?.isAdmin != true) throw ApiException.forbidden("You do not have permission to access this resource.")
    }

    private fun BookingEntity.toEvent() = BookingEvent(id, facilityName, conduct, description, pocName, pocPhone, startDateTime, endDateTime, userPhone)
}
