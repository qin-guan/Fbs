package sg.from.fbs.webapi.integration

import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import org.springframework.stereotype.Component
import org.springframework.web.client.RestClient
import org.springframework.web.util.UriUtils
import sg.from.fbs.webapi.config.GoogleProperties
import java.nio.charset.StandardCharsets

@Component
class GoogleCalendarClient(
    private val props: GoogleProperties,
    private val restClient: RestClient,
    private val objectMapper: ObjectMapper,
    private val tokens: GoogleAccessTokenProvider,
) {
    fun listAllEvents(calendarId: String): List<JsonNode> {
        val out = mutableListOf<JsonNode>()
        var token: String? = null
        do {
            val base = "https://www.googleapis.com/calendar/v3/calendars/${UriUtils.encodePathSegment(calendarId, StandardCharsets.UTF_8)}/events"
            val uri = if (token == null) base else "$base?pageToken=${UriUtils.encodeQueryParam(token, StandardCharsets.UTF_8)}"
            val body = restClient.get()
                .uri(uri)
                .headers { it.setBearerAuth(tokens.calendarToken()) }
                .retrieve()
                .body(String::class.java) ?: "{}"
            val json = objectMapper.readTree(body)
            out += json.path("items").toList()
            token = json.path("nextPageToken").asText().ifBlank { null }
        } while (token != null)
        return out
    }

    fun insert(calendarId: String, payload: Map<String, Any?>) {
        restClient.post()
            .uri("https://www.googleapis.com/calendar/v3/calendars/${UriUtils.encodePathSegment(calendarId, StandardCharsets.UTF_8)}/events")
            .headers { it.setBearerAuth(tokens.calendarToken()) }
            .body(payload)
            .retrieve()
            .toBodilessEntity()
    }

    fun update(calendarId: String, eventId: String, payload: Map<String, Any?>) {
        restClient.put()
            .uri("https://www.googleapis.com/calendar/v3/calendars/${UriUtils.encodePathSegment(calendarId, StandardCharsets.UTF_8)}/events/${UriUtils.encodePathSegment(eventId, StandardCharsets.UTF_8)}")
            .headers { it.setBearerAuth(tokens.calendarToken()) }
            .body(payload)
            .retrieve()
            .toBodilessEntity()
    }

    fun delete(calendarId: String, eventId: String) {
        restClient.delete()
            .uri("https://www.googleapis.com/calendar/v3/calendars/${UriUtils.encodePathSegment(calendarId, StandardCharsets.UTF_8)}/events/${UriUtils.encodePathSegment(eventId, StandardCharsets.UTF_8)}")
            .headers { it.setBearerAuth(tokens.calendarToken()) }
            .retrieve()
            .toBodilessEntity()
    }
}
