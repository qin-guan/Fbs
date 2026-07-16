package sg.from.fbs.webapi.repository

import org.springframework.stereotype.Repository
import sg.from.fbs.webapi.entity.OtpEntity
import sg.from.fbs.webapi.integration.GoogleSheetsClient
import java.time.OffsetDateTime

@Repository
class OtpRepository(private val sheets: GoogleSheetsClient) {
    private val header = listOf("Phone", "Code", "CreatedAt")

    fun getList(): List<OtpEntity> {
        val rows = sheets.readValues("OTPs")
        require(rows.firstOrNull() == header) { "Unexpected headers for Otp sheet" }
        return rows.drop(1).mapIndexed { idx, row ->
            OtpEntity(
                row = idx + 2,
                phone = row.getOrNull(0),
                code = row.getOrNull(1),
                createdAt = row.getOrNull(2)?.let(OffsetDateTime::parse),
            )
        }
    }

    fun findByPhone(phone: String): OtpEntity? = getList().singleOrNull { it.phone == phone }

    fun insert(entity: OtpEntity): OtpEntity {
        sheets.appendRange("OTPs", listOf(listOf(entity.phone, entity.code, entity.createdAt.toString())))
        return getList().single { it.phone == entity.phone }
    }

    fun update(entity: OtpEntity): OtpEntity {
        sheets.updateRange("OTPs!A${entity.row}:C${entity.row}", listOf(listOf(entity.phone, entity.code, entity.createdAt.toString())))
        return entity
    }

    fun deleteByPhone(phone: String) {
        val entity = getList().single { it.phone == phone }
        sheets.deleteRow("OTPs", entity.row)
    }
}
