package sg.from.fbs.repository

import com.google.api.services.sheets.v4.Sheets
import com.google.api.services.sheets.v4.model.*
import org.springframework.stereotype.Repository
import sg.from.fbs.config.GoogleProperties
import sg.from.fbs.entities.Otp
import java.time.OffsetDateTime

@Repository
class OtpRepository(
    private val sheetsService: Sheets,
    private val options: GoogleProperties
) {

    private val header = listOf("Phone", "Code", "CreatedAt")

    fun getList(): List<Otp> {
        val response = sheetsService.spreadsheets().values()
            .get(options.spreadsheetId, "OTPs")
            .execute()

        val values = response.getValues()
        if (values == null || values.isEmpty()) return emptyList()

        val actualHeader = values.first().map { it.toString() }
        if (actualHeader != header) {
            throw Exception("Unexpected headers for Otp sheet")
        }

        return values.drop(1).mapIndexed { idx, row ->
            Otp(
                row = idx + 2,
                phone = row.getOrNull(0) as? String,
                code = row.getOrNull(1) as? String,
                createdAt = (row.getOrNull(2) as? String)?.let { OffsetDateTime.parse(it) }
            )
        }
    }

    fun find(predicate: (Otp) -> Boolean): Otp? {
        return getList().find(predicate)
    }

    fun get(predicate: (Otp) -> Boolean): Otp {
        return getList().first(predicate)
    }

    fun insert(entity: Otp): Otp {
        val body = ValueRange().setValues(
            listOf(
                listOf(
                    entity.phone,
                    entity.code,
                    entity.createdAt.toString()
                )
            )
        )

        sheetsService.spreadsheets().values()
            .append(options.spreadsheetId, "OTPs", body)
            .setValueInputOption("RAW")
            .execute()

        return get { it.phone == entity.phone }
    }

    fun update(entity: Otp): Otp {
        val body = ValueRange().setValues(
            listOf(
                listOf(
                    entity.phone,
                    entity.code,
                    entity.createdAt.toString()
                )
            )
        )

        sheetsService.spreadsheets().values()
            .update(options.spreadsheetId, "OTPs!A${entity.row}:C${entity.row}", body)
            .setValueInputOption("RAW")
            .execute()

        return entity
    }

    fun delete(predicate: (Otp) -> Boolean) {
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
        val sheet = spreadsheet.sheets.single { it.properties.title == "OTPs" }
        return sheet.properties.sheetId
    }
}