package sg.from.fbs.repository

import com.google.api.services.sheets.v4.Sheets
import com.google.api.services.sheets.v4.model.*
import org.springframework.cache.annotation.CacheEvict
import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.config.GoogleProperties
import sg.from.fbs.entities.User
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Lazy

@Repository
class UserRepository(
    private val sheetsService: Sheets,
    private val options: GoogleProperties
) {
    @Autowired
    @Lazy
    lateinit var self: UserRepository

    private val header = listOf("Unit", "Name", "Phone", "TelegramChatId", "NotificationGroup", "IsAdmin")

    @Cacheable("Users")
    fun getList(): List<User> {
        val response = sheetsService.spreadsheets().values()
            .get(options.spreadsheetId, "Users")
            .execute()

        val values = response.getValues()
        if (values == null || values.isEmpty()) return emptyList()

        val actualHeader = values.first().map { it.toString() }
        if (actualHeader != header) {
            throw Exception("Unexpected headers for Users sheet")
        }

        return values.drop(1).mapIndexed { idx, row ->
            User(
                row = idx + 2,
                unit = row.getOrNull(0) as? String,
                name = row.getOrNull(1) as? String,
                phone = row.getOrNull(2) as? String,
                telegramChatId = row.getOrNull(3) as? String,
                notificationGroup = row.getOrNull(4) as? String,
                isAdmin = (row.getOrNull(5) as? String) == "TRUE"
            )
        }
    }

    fun find(predicate: (User) -> Boolean): User? {
        return self.getList().find(predicate)
    }

    fun get(predicate: (User) -> Boolean): User {
        return self.getList().first(predicate)
    }

    fun insert(entity: User): User {
        throw NotImplementedError()
    }

    @CacheEvict(value = ["Users"], allEntries = true)
    fun update(entity: User): User {
        val body = ValueRange().setValues(
            listOf(
                listOf(
                    entity.unit,
                    entity.name,
                    entity.phone,
                    entity.telegramChatId,
                    entity.notificationGroup,
                    entity.isAdmin.toString().uppercase()
                )
            )
        )

        sheetsService.spreadsheets().values()
            .update(options.spreadsheetId, "Users!A${entity.row}:F${entity.row}", body)
            .setValueInputOption("RAW")
            .execute()

        return entity
    }

    @CacheEvict(value = ["Users"], allEntries = true)
    fun delete(predicate: (User) -> Boolean) {
        val entity = get(predicate)
        val sheetId = getSheetId()

        val request = Request().setDeleteDimension(
            DeleteDimensionRequest().setRange(
                DimensionRange()
                    .setSheetId(sheetId)
                    .setDimension("ROWS")
                    .setStartIndex(entity.row - 1)
                    .setEndIndex(entity.row)
            )
        )

        sheetsService.spreadsheets().batchUpdate(
            options.spreadsheetId,
            BatchUpdateSpreadsheetRequest().setRequests(listOf(request))
        ).execute()
    }

    private fun getSheetId(): Int {
        val spreadsheet = sheetsService.spreadsheets().get(options.spreadsheetId).execute()
        val sheet = spreadsheet.sheets.single { it.properties.title == "Users" }
        return sheet.properties.sheetId
    }
}