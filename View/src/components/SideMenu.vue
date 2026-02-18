<script setup>
import { ref, computed } from "vue";
import { useRouter, useRoute } from "vue-router";
import {
  Setting,
  User,
  Menu as MenuIcon,
  Expand,
  Fold,
  Search,
  Document,
} from "@element-plus/icons-vue";

const router = useRouter();
const route = useRoute();

const isCollapsed = ref(false);

const toggleCollapse = () => {
  isCollapsed.value = !isCollapsed.value;
};

// 所有菜单项
const allMenuItems = [
  { index: "1", icon: User, label: "用户管理", path: "/users" },
  { index: "2", icon: Search, label: "COM 扫描", path: "/com-scan" },
  { index: "3", icon: Setting, label: "COM 分配", path: "/com-allocation" },
  { index: "4", icon: MenuIcon, label: "短信/来电", path: "/sms-management" },

  { index: "6", icon: Document, label: "记事本", path: "/notes" },
];

// 获取用户角色（从 localStorage 获取）
const getUserRole = () => {
  try {
    const userInfo = localStorage.getItem("sms_user");
    if (userInfo) {
      const user = JSON.parse(userInfo);
      // UserRole 枚举: 0 = User, 1 = Admin
      return user.role === 1;
    }
  } catch (error) {
    console.error("获取用户角色失败:", error);
  }
  return false; // 默认为普通用户
};

// 根据角色过滤菜单
const menuItems = computed(() => {
  const isAdmin = getUserRole();

  // 管理员拥有所有菜单
  if (isAdmin) {
    return allMenuItems;
  }

  // 非管理员根据环境变量配置过滤菜单
  const nonAdminMenus = import.meta.env.VITE_NON_ADMIN_MENUS || "1";
  const allowedIndexes = nonAdminMenus.split(",").map((i) => i.trim());

  return allMenuItems.filter((item) => allowedIndexes.includes(item.index));
});

// 根据当前路由获取激活的菜单项
const getActiveIndex = () => {
  const currentPath = route.path;
  const normalizedPath =
    currentPath === "/call-hangup-records" ? "/sms-management" : currentPath;
  const item = menuItems.value.find((item) => item.path === normalizedPath);
  return item ? item.index : "1";
};

const activeIndex = ref(getActiveIndex());

// 菜单点击处理
const handleMenuSelect = (index) => {
  const item = menuItems.value.find((item) => item.index === index);
  if (item && item.path) {
    router.push(item.path);
    activeIndex.value = index;
  }
};
</script>

<template>
  <el-aside
    :width="isCollapsed ? '64px' : '220px'"
    class="app-aside"
    :class="{ collapsed: isCollapsed }"
  >
    <div class="brand">
      <el-icon><MenuIcon /></el-icon>
      <span v-show="!isCollapsed">SMS 管理后台</span>
    </div>

    <el-menu
      class="app-menu"
      :default-active="activeIndex"
      :collapse="isCollapsed"
      :collapse-transition="false"
      @select="handleMenuSelect"
    >
      <el-menu-item
        v-for="item in menuItems"
        :key="item.index"
        :index="item.index"
      >
        <el-icon><component :is="item.icon" /></el-icon>
        <template #title>{{ item.label }}</template>
      </el-menu-item>
    </el-menu>

    <div class="collapse-toggle" @click="toggleCollapse">
      <el-icon>
        <Expand v-if="isCollapsed" />
        <Fold v-else />
      </el-icon>
      <span v-show="!isCollapsed">收起菜单</span>
    </div>
  </el-aside>
</template>

<style scoped>
.app-aside {
  background: linear-gradient(180deg, #1f2d3d 0%, #1a2533 100%);
  color: #fff;
  display: flex;
  flex-direction: column;
  transition: width 0.3s ease;
  box-shadow: 2px 0 8px rgba(0, 0, 0, 0.1);
}

.brand {
  height: 56px;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 0 20px;
  font-weight: 600;
  font-size: 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  white-space: nowrap;
  overflow: hidden;
}

.brand .el-icon {
  font-size: 20px;
  color: #409eff;
}

.app-menu {
  flex: 1;
  border-right: none;
  background: transparent;
  overflow-y: auto;
  overflow-x: hidden;
}

/* 菜单项样式优化 */
.app-menu :deep(.el-menu-item) {
  color: rgba(255, 255, 255, 0.75);
  background: transparent;
  transition: all 0.3s ease;
  margin: 4px 8px;
  border-radius: 6px;
  height: 48px;
  line-height: 48px;
}

.app-menu :deep(.el-menu-item:hover) {
  color: #fff;
  background: rgba(64, 158, 255, 0.15);
}

.app-menu :deep(.el-menu-item.is-active) {
  color: #fff;
  background: linear-gradient(90deg, #409eff 0%, #66b1ff 100%);
  box-shadow: 0 2px 8px rgba(64, 158, 255, 0.3);
}

.app-menu :deep(.el-menu-item .el-icon) {
  font-size: 18px;
  color: inherit;
}

/* 收缩状态样式 */
.app-menu.el-menu--collapse :deep(.el-menu-item) {
  padding: 0 !important;
  margin: 4px 0;
  display: flex;
  justify-content: center;
  align-items: center;
  width: 64px;
}

.app-menu.el-menu--collapse :deep(.el-menu-item .el-icon) {
  margin: 0 !important;
}

.app-aside.collapsed .brand {
  justify-content: center;
  padding: 0;
}

.collapse-toggle {
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 0 20px;
  cursor: pointer;
  color: rgba(255, 255, 255, 0.65);
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.3s ease;
  white-space: nowrap;
  overflow: hidden;
}

.app-aside.collapsed .collapse-toggle {
  padding: 0;
  justify-content: center;
}

.collapse-toggle:hover {
  color: #fff;
  background: rgba(255, 255, 255, 0.05);
}

.collapse-toggle .el-icon {
  font-size: 18px;
}

/* 滚动条样式 */
.app-menu::-webkit-scrollbar {
  width: 6px;
}

.app-menu::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.2);
  border-radius: 3px;
}

.app-menu::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.3);
}
</style>
