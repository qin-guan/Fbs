package sg.from.fbs.config

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Test
import org.springframework.mock.web.MockFilterChain
import org.springframework.mock.web.MockHttpServletRequest
import org.springframework.mock.web.MockHttpServletResponse

class AuthFilterTest {
    private val filter = AuthFilter()

    @Test
    fun `allows Scalar UI without authentication`() {
        assertPublic("/scalar")
        assertPublic("/scalar/")
    }

    @Test
    fun `allows OpenAPI documents without authentication`() {
        assertPublic("/v3/api-docs")
        assertPublic("/v3/api-docs.yaml")
        assertPublic("/v3/api-docs/public")
    }

    @Test
    fun `does not expose paths that only resemble documentation paths`() {
        assertUnauthorized("/scalar-private")
        assertUnauthorized("/v3/api-docs-private")
    }

    @Test
    fun `continues to require authentication for application endpoints`() {
        assertUnauthorized("/Facilities")
    }

    private fun assertPublic(uri: String) {
        val request = MockHttpServletRequest("GET", uri)
        val response = MockHttpServletResponse()
        val chain = MockFilterChain()

        filter.doFilter(request, response, chain)

        assertEquals(200, response.status)
        assertNotNull(chain.request)
    }

    private fun assertUnauthorized(uri: String) {
        val request = MockHttpServletRequest("GET", uri)
        val response = MockHttpServletResponse()
        val chain = MockFilterChain()

        filter.doFilter(request, response, chain)

        assertEquals(401, response.status)
        assertEquals(null, chain.request)
    }
}
