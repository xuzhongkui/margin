<script setup>
import { reactive, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElMessage } from "element-plus";
import { login } from "../services/api";

const router = useRouter();
const route = useRoute();
const loading = ref(false);
const form = reactive({
  userName: "",
  password: "",
});

const submit = async () => {
  if (loading.value) {
    return;
  }

  if (!form.userName || !form.password) {
    ElMessage.warning("请输入用户名和密码");
    return;
  }

  loading.value = true;
  try {
    await login(form.userName, form.password);
    const redirect = route.query.redirect || "/";
    router.replace(redirect);
  } catch (error) {
    ElMessage.error("账号或密码错误");
  } finally {
    loading.value = false;
  }
};
</script>

<template>
  <div class="login-page">
    <el-card class="login-card" shadow="never">
      <template #header>
        <div class="login-title">SMS 管理后台</div>
      </template>
      <el-form label-position="top" @submit.prevent="submit">
        <el-form-item label="用户名">
          <el-input v-model="form.userName" placeholder="请输入用户名" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input
            v-model="form.password"
            type="password"
            placeholder="请输入密码"
            show-password
          />
        </el-form-item>
        <el-button type="primary" :loading="loading" class="login-button" @click="submit">
          登录
        </el-button>
      </el-form>
    </el-card>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f5f7fa;
  padding: 24px;
}

.login-card {
  width: 360px;
}

.login-title {
  font-size: 18px;
  font-weight: 600;
  text-align: center;
}

.login-button {
  width: 100%;
}
</style>
