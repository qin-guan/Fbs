import { graphql } from '~/gql'

export const queries = {
  me: graphql(`
    query me {
      me {
        phone
      }
    }
  `),

  meWithName: graphql(`
    query meWithName {
      me {
        name
        phone
      }
    }
  `),

  facilities: graphql(`
    query facilities {
      facilities {
        name
      }
    }
  `),

  slots: graphql(`
    query slots($facility: String!, $startDate: DateTime!, $endDate: DateTime!) {
      facilities(name: $facility) {
        name
        availableTimeSlots(start: $startDate, end: $endDate) {
          startDateTime
          endDateTime
          booking {
            conduct
            description
            pocName
            pocPhone
          }
        }
      }
    }
  `),

  bookings: graphql(`
    query bookings($id: UUID, $userPhone: String) {
      bookings(id: $id, userPhone: $userPhone) {
        id
        conduct
        description
        startDateTime
        endDateTime
        facilityName
        pocName
        pocPhone
      }
    }
  `),
}

export const mutations = {
  deleteBooking: graphql(`
    mutation deleteBooking($id: UUID!) {
      deleteBooking(id: $id)
    }
  `),
  updateBooking: graphql(`
    mutation updateBooking(
      $id: UUID!
      $conduct: String!
      $description: String!
      $facilityName: String!
      $pocName: String!
      $pocPhone: String!
    ) {

      updateBooking(
        id: $id
        conduct: $conduct
        description: $description
        facilityName: $facilityName
        pocName: $pocName
        pocPhone: $pocPhone
      ) {
        id
        description
        startDateTime
        endDateTime
        facilityName
        pocName
        pocPhone
      }

    }
  `),

  insertBooking: graphql(`
    mutation insertBooking(
      $conduct: String!
      $description: String!
      $facilityName: String!
      $pocName: String!
      $pocPhone: String!
      $startDateTime: DateTime!
      $endDateTime: DateTime!
    ) {

      insertBooking(
        conduct: $conduct
        description: $description
        facilityName: $facilityName
        pocName: $pocName
        pocPhone: $pocPhone
        startDateTime: $startDateTime
        endDateTime: $endDateTime
      ) {
        id
        description
        startDateTime
        endDateTime
        facilityName
        pocName
        pocPhone
      }

    }
  `),
}
