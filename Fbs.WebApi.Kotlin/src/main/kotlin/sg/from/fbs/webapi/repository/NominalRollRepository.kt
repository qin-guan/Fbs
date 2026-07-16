package sg.from.fbs.webapi.repository

import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.webapi.entity.NominalRollEntity
import sg.from.fbs.webapi.integration.GoogleSheetsClient

@Repository
class NominalRollRepository(private val sheets: GoogleSheetsClient) {
    private val header = listOf("Name", "Unit", "Phone")

    @Cacheable("Nominal Roll")
    fun getList(): List<NominalRollEntity> {
        val rows = sheets.readValues("Nominal Roll")
        require(rows.firstOrNull() == header) { "Unexpected headers in Nominal Roll table" }
        return rows.drop(1).mapIndexed { idx, row ->
            NominalRollEntity(
                row = idx + 2,
                name = row.getOrNull(0),
                unit = row.getOrNull(1),
                phone = row.getOrNull(2),
            )
        }
    }
}
