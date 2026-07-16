package sg.from.fbs.entities

data class User(
    var row: Int = 0,
    var unit: String? = null,
    var name: String? = null,
    var phone: String? = null,
    var telegramChatId: String? = null,
    var notificationGroup: String? = null,
    var isAdmin: Boolean = false
)