package sg.from.fbs.webapi

import org.springframework.boot.autoconfigure.SpringBootApplication
import org.springframework.boot.context.properties.ConfigurationPropertiesScan
import org.springframework.boot.runApplication

@SpringBootApplication
@ConfigurationPropertiesScan
class FbsWebApiKotlinApplication

fun main(args: Array<String>) {
    runApplication<FbsWebApiKotlinApplication>(*args)
}
