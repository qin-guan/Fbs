package sg.from.fbs.webapi.repository

import org.springframework.cache.annotation.CacheEvict
import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.webapi.entity.UserEntity
import sg.from.fbs.webapi.integration.GoogleSheetsClient

@Repository
class UserRepository(private val sheets: GoogleSheetsClient) {
    private val header = listOf("Unit", "Name", "Phone", "TelegramChatId", "NotificationGroup", "IsAdmin")

    @Cacheable("Users")
    fun getList(): List<UserEntity> {
        val rows = sheets.readValues("Users")
        require(rows.firstOrNull() == header) { "Unexpected headers for Users sheet" }
        return rows.drop(1).mapIndexed { idx, row ->
            UserEntity(
                row = idx + 2,
                unit = row.getOrNull(0),
                name = row.getOrNull(1),
                phone = row.getOrNull(2),
                telegramChatId = row.getOrNull(3),
                notificationGroup = row.getOrNull(4),
                isAdmin = row.getOrNull(5) == "TRUE",
            )
        }
    }

    fun findByPhone(phone: String?): UserEntity? = getList().singleOrNull { it.phone == phone }

    fun getByPhone(phone: String?): UserEntity = getList().single { it.phone == phone }

    @CacheEvict("Users", allEntries = true)
    fun update(entity: UserEntity): UserEntity {
        sheets.updateRange(
            "Users!A${entity.row}:F${entity.row}",
            listOf(
                listOf(
                    entity.unit,
                    entity.name,
                    entity.phone,
                    entity.telegramChatId,
                    entity.notificationGroup,
                    entity.isAdmin.toString().uppercase(),
                ),
            ),
        )
        return entity
    }

    @CacheEvict("Users", allEntries = true)
    fun delete(phone: String) {
        val entity = getByPhone(phone)
        sheets.deleteRow("Users", entity.row)
    }
}
