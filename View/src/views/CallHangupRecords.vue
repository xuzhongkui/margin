<template>
  <SmsManagement default-tab="hangup" />
</template>

<script>
import SmsManagement from "./SmsManagement.vue";

export default {
  name: "CallHangupRecords",
  components: {
    SmsManagement,
  },
};
</script>
