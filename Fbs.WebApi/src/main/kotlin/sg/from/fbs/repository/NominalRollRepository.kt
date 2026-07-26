package sg.from.fbs.repository

import com.google.api.services.sheets.v4.Sheets
import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.config.GoogleProperties
import sg.from.fbs.entities.NominalRoll
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Lazy

@Repository
class NominalRollRepository(
    private val sheetsService: Sheets,
    private val options: GoogleProperties
) {
    @Autowired
    @Lazy
    lateinit var self: NominalRollRepository

    private val header = listOf("Name", "Unit", "Phone")

    @Cacheable("Nominal Roll")
    fun getList(): List<NominalRoll> {
        val response = sheetsService.spreadsheets().values()
            .get(options.spreadsheetId, "Nominal Roll")
            .execute()

        val values = response.getValues()
        if (values == null || values.isEmpty()) return emptyList()

        val actualHeader = values.first().map { it.toString() }
        if (actualHeader != header) {
            throw Exception("Unexpected headers in Nominal Roll table")
        }

        return values.drop(1).mapIndexed { idx, row ->
            NominalRoll(
                row = idx + 2,
                name = row.getOrNull(0) as? String,
                unit = row.getOrNull(1) as? String,
                phone = row.getOrNull(2) as? String
            )
        }
    }

    fun find(predicate: (NominalRoll) -> Boolean): NominalRoll? {
        return self.getList().find(predicate)
    }

    fun get(predicate: (NominalRoll) -> Boolean): NominalRoll {
        return self.getList().first(predicate)
    }

    fun insert(entity: NominalRoll): NominalRoll {
        throw NotImplementedError()
    }

    fun update(entity: NominalRoll): NominalRoll {
        throw NotImplementedError()
    }

    fun delete(predicate: (NominalRoll) -> Boolean) {
        throw NotImplementedError()
    }
}