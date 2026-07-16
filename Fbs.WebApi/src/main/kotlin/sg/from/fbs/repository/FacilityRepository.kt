package sg.from.fbs.repository

import com.google.api.services.sheets.v4.Sheets
import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.config.GoogleProperties
import sg.from.fbs.entities.Facility
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Lazy

@Repository
class FacilityRepository(
    private val sheetsService: Sheets,
    private val options: GoogleProperties
) {
    @Autowired
    @Lazy
    lateinit var self: FacilityRepository

    private val header = listOf("Name", "Group", "Scope")

    @Cacheable("Facilities")
    fun getList(): List<Facility> {
        val response = sheetsService.spreadsheets().values()
            .get(options.spreadsheetId, "Facilities")
            .execute()

        val values = response.getValues()
        if (values == null || values.isEmpty()) return emptyList()

        val actualHeader = values.first().map { it.toString() }
        if (actualHeader != header) {
            throw Exception("Unexpected headers in Facilities table")
        }

        return values.drop(1).mapIndexed { idx, row ->
            Facility(
                row = idx + 2,
                name = row.getOrNull(0) as? String,
                group = row.getOrNull(1) as? String,
                scope = (row.getOrNull(2) as? String)
                    ?.split(",")
                    ?.map { it.trim() }
                    ?.filter { it.isNotEmpty() }
            )
        }
    }

    fun find(predicate: (Facility) -> Boolean): Facility? {
        return self.getList().find(predicate)
    }

    fun get(predicate: (Facility) -> Boolean): Facility {
        return self.getList().first(predicate)
    }

    fun insert(entity: Facility): Facility {
        throw NotImplementedError()
    }

    fun update(entity: Facility): Facility {
        throw NotImplementedError()
    }

    fun delete(predicate: (Facility) -> Boolean) {
        throw NotImplementedError()
    }
}