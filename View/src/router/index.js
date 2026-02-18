import { createRouter, createWebHistory } from "vue-router";
import Dashboard from "../views/Dashboard.vue";
import Login from "../views/Login.vue";
import UserManagement from "../views/UserManagement.vue";
import ComScan from "../views/ComScan.vue";
import ComAllocation from "../views/ComAllocation.vue";
import SmsManagement from "../views/SmsManagement.vue";
import CallHangupRecords from "../views/CallHangupRecords.vue";
import NoteManagement from "../views/NoteManagement.vue";
import { isAuthenticated } from "../services/auth";
import { onUnauthorized } from "../services/unauthorized-events";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: "/login",
      component: Login,
    },
    {
      path: "/",
      component: Dashboard,
      meta: { requiresAuth: true },
      redirect: "/users",
      children: [
        {
          path: "users",
          component: UserManagement,
          meta: { requiresAuth: true },
        },
        {
          path: "com-scan",
          component: ComScan,
          meta: { requiresAuth: true },
        },
        {
          path: "com-allocation",
          component: ComAllocation,
          meta: { requiresAuth: true },
        },
        {
          path: "sms-management",
          component: SmsManagement,
          meta: { requiresAuth: true },
        },
        {
          path: "call-hangup-records",
          component: CallHangupRecords,
          meta: { requiresAuth: true },
        },
        {
          path: "notes",
          component: NoteManagement,
          meta: { requiresAuth: true },
        },
      ],
    },
  ],
});

router.beforeEach((to) => {
  if (to.meta.requiresAuth && !isAuthenticated()) {
    return { path: "/login", query: { redirect: to.fullPath } };
  }

  if (to.path === "/login" && isAuthenticated()) {
    return { path: "/" };
  }

  return true;
});
onUnauthorized(() => {
  if (router.currentRoute.value.path !== "/login") {
    router.push({
      path: "/login",
      query: { redirect: router.currentRoute.value.fullPath || "/" },
    });
  }
});

export default router;
