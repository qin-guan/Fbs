package sg.from.fbs.entities

data class Facility(
    var row: Int = 0,
    var name: String? = null,
    var group: String? = null,
    var scope: List<String>? = null
) {
    val availableForAll: Boolean
        get() = scope?.contains("All") == true
}