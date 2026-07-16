package sg.from.fbs.controllers

import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.entities.NominalRoll
import sg.from.fbs.repository.NominalRollRepository

@RestController
@RequestMapping("/NominalRoll")
class NominalRollController(
    private val nominalRollRepository: NominalRollRepository
) {
    @GetMapping
    fun getNominalRoll(): ResponseEntity<List<NominalRoll>> {
        return ResponseEntity.ok(nominalRollRepository.getList())
    }
}