package sg.from.fbs.controllers

import org.springframework.cache.annotation.CacheEvict
import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RestController

@RestController
@RequestMapping("/Cache")
class CacheController {

    @GetMapping("/Purge")
    @CacheEvict(value = ["Users", "Bookings", "Facilities", "Nominal Roll"], allEntries = true)
    fun purge(): ResponseEntity<Any> {
        return ResponseEntity.noContent().build()
    }
}