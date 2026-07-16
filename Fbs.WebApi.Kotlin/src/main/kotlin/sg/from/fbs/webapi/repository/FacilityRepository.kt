package sg.from.fbs.webapi.repository

import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.webapi.entity.FacilityEntity
import sg.from.fbs.webapi.integration.GoogleSheetsClient

@Repository
class FacilityRepository(private val sheets: GoogleSheetsClient) {
    private val header = listOf("Name", "Group", "Scope")

    @Cacheable("Facilities")
    fun getList(): List<FacilityEntity> {
        val rows = sheets.readValues("Facilities")
        require(rows.firstOrNull() == header) { "Unexpected headers in Facilities table" }
        return rows.drop(1).mapIndexed { idx, row ->
            FacilityEntity(
                row = idx + 2,
                name = row.getOrNull(0),
                group = row.getOrNull(1),
                scope = row.getOrNull(2)?.split(",")?.map { it.trim() }?.filter { it.isNotBlank() },
            )
        }
    }

    fun findByName(name: String): FacilityEntity? = getList().singleOrNull { it.name == name }

    fun getByName(name: String): FacilityEntity = getList().single { it.name == name }
}
