package sg.from.fbs.webapi.integration

import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import org.springframework.stereotype.Component
import org.springframework.web.client.RestClient
import org.springframework.web.util.UriUtils
import sg.from.fbs.webapi.config.GoogleProperties
import java.nio.charset.StandardCharsets

@Component
class GoogleSheetsClient(
    private val props: GoogleProperties,
    private val restClient: RestClient,
    private val objectMapper: ObjectMapper,
    private val tokens: GoogleAccessTokenProvider,
) {
    fun readValues(range: String): List<List<String>> {
        val encodedRange = UriUtils.encodePathSegment(range, StandardCharsets.UTF_8)
        val body = restClient.get()
            .uri("https://sheets.googleapis.com/v4/spreadsheets/${props.spreadsheetId}/values/$encodedRange")
            .headers { it.setBearerAuth(tokens.sheetsToken()) }
            .retrieve()
            .body(String::class.java) ?: "{}"
        val values = objectMapper.readTree(body).path("values")
        return values.map { row -> row.map { it.asText() } }
    }

    fun updateRange(range: String, values: List<List<String?>>) {
        val encodedRange = UriUtils.encodePathSegment(range, StandardCharsets.UTF_8)
        val payload = mapOf("values" to values)
        restClient.put()
            .uri("https://sheets.googleapis.com/v4/spreadsheets/${props.spreadsheetId}/values/$encodedRange?valueInputOption=RAW")
            .headers { it.setBearerAuth(tokens.sheetsToken()) }
            .body(payload)
            .retrieve()
            .toBodilessEntity()
    }

    fun appendRange(range: String, values: List<List<String?>>) {
        val encodedRange = UriUtils.encodePathSegment(range, StandardCharsets.UTF_8)
        val payload = mapOf("values" to values)
        restClient.post()
            .uri("https://sheets.googleapis.com/v4/spreadsheets/${props.spreadsheetId}/values/$encodedRange:append?valueInputOption=RAW")
            .headers { it.setBearerAuth(tokens.sheetsToken()) }
            .body(payload)
            .retrieve()
            .toBodilessEntity()
    }

    fun deleteRow(sheetName: String, oneBasedRow: Int) {
        val sheetId = sheetId(sheetName)
        val payload = mapOf(
            "requests" to listOf(
                mapOf(
                    "deleteDimension" to mapOf(
                        "range" to mapOf(
                            "sheetId" to sheetId,
                            "dimension" to "ROWS",
                            "startIndex" to oneBasedRow - 1,
                            "endIndex" to oneBasedRow,
                        ),
                    ),
                ),
            ),
        )
        restClient.post()
            .uri("https://sheets.googleapis.com/v4/spreadsheets/${props.spreadsheetId}:batchUpdate")
            .headers { it.setBearerAuth(tokens.sheetsToken()) }
            .body(payload)
            .retrieve()
            .toBodilessEntity()
    }

    private fun sheetId(sheetName: String): Int {
        val body = restClient.get()
            .uri("https://sheets.googleapis.com/v4/spreadsheets/${props.spreadsheetId}?fields=sheets(properties(sheetId,title))")
            .headers { it.setBearerAuth(tokens.sheetsToken()) }
            .retrieve()
            .body(String::class.java) ?: "{}"
        val json: JsonNode = objectMapper.readTree(body)
        return json.path("sheets")
            .firstOrNull { it.path("properties").path("title").asText() == sheetName }
            ?.path("properties")
            ?.path("sheetId")
            ?.asInt()
            ?: error("Sheet not found: $sheetName")
    }
}
